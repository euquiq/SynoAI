﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using SynoAI.Models;
using SynoAI.Notifiers;
using SynoAI.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using SynoAI.Extensions;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SynoAI.Controllers
{
    /// <summary>
    /// Controller triggered on a motion alert from synology, which will act as a bridge between the Synology camera and DeepStack AI.
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class CameraController : ControllerBase
    {
        private readonly IAIService _aiService;
        private readonly ISynologyService _synologyService;
        private readonly ILogger<CameraController> _logger;
        private readonly ILogger<ISnapshotManager>  _snapshotManagerLogger;

        private static ConcurrentDictionary<string, DateTime> _lastCameraChecks = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public CameraController(IAIService aiService, ISynologyService synologyService, ILogger<CameraController> logger, ILogger<ISnapshotManager> snapshotManagerLogger)
        {
            _aiService = aiService;
            _synologyService = synologyService;
            _logger = logger;
            _snapshotManagerLogger = snapshotManagerLogger;
        }

        /// <summary>
        /// Called by the Synology motion alert hook.
        /// </summary>
        /// <param name="id">The name of the camera.</param>
        [HttpGet]
        [Route("{id}")]
        public async void Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentNullException(nameof(id));
            }

            // Fetch the camera
            Camera camera = Config.Cameras.FirstOrDefault(x => x.Name.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (camera == null)
            {
                _logger.LogError($"The camera with the name '{id}' was not found.");
                return;
            }

            // Get the min X and Y values for object; initialize snapshots counter.
            int minX = camera.GetMinSizeX();
            int minY = camera.GetMinSizeY();
            int snapshotCount= 1;

            // Create the stopwatches for reporting timings
            Stopwatch overallStopwatch = Stopwatch.StartNew();

            //Start bucle for asking snapshots until a valid prediction is found or MaxSnapshots is reached
            while (snapshotCount > 0 && snapshotCount <= Config.MaxSnapshots) 
            {
                _logger.LogInformation($"Snapshot {snapshotCount} of {Config.MaxSnapshots} asked at EVENT TIME {overallStopwatch.ElapsedMilliseconds}ms.");
                // Take the snapshot from Surveillance Station
                byte[] snapshot = await GetSnapshot(id);
                _logger.LogInformation($"Snapshot {snapshotCount} of {Config.MaxSnapshots} received at EVENT TIME {overallStopwatch.ElapsedMilliseconds}ms.");

                //See if the image needs to be rotated (or further processing in the future ?) previous to being analyzed by AI
                snapshot = PreProcessSnapshot(camera, snapshot);

                // Use the AI to get the valid predictions and then get all the valid predictions, which are all the AI predictions where the result from the AI is 
                // in the list of types and where the size of the object is bigger than the defined value.
                IEnumerable<AIPrediction> predictions = await GetAIPredications(camera, snapshot);

                _logger.LogInformation($"Snapshot {snapshotCount} of {Config.MaxSnapshots} processed {predictions.Count()} objects at EVENT TIME {overallStopwatch.ElapsedMilliseconds}ms.");
                
                if (predictions != null)
                {
                    IEnumerable<AIPrediction> validPredictions = predictions.Where(x =>
                        camera.Types.Contains(x.Label, StringComparer.OrdinalIgnoreCase) &&     // Is a type we care about
                        x.SizeX >= minX && x.SizeY >= minY)                                     // Is bigger than the minimum size
                        .ToList();

                    if (validPredictions.Count() > 0)
                    {

                        // Because we don't want to process the image if it isn't even required, then we pass the snapshot manager to the notifiers. It will then perform 
                        // the necessary actions when it's GetImage method is called.
                        SnapshotManager snapshotManager = new SnapshotManager(snapshot, predictions, validPredictions, _snapshotManagerLogger);

                        // Save the original unprocessed image if required
                        if (Config.SaveOriginalSnapshot)
                        {
                            _logger.LogInformation($"{id}: Saving original image");
                            SnapshotManager.SaveOriginalImage(_logger, camera, snapshot);
                        }

                        // Generate text for notifications                  
                        IList<String> labels = new List<String>();

                        if (Config.AlternativeLabelling && Config.DrawMode == DrawMode.Matches)
                        {
                            if (validPredictions.Count() == 1) 
                            {
                                decimal confidence = Math.Round(validPredictions.First().Confidence, 0, MidpointRounding.AwayFromZero);
                                labels.Add($"{validPredictions.First().Label.FirstCharToUpper()} {confidence}%");
                            }
                            else 
                            {
                                //Since there is more than one object detected, include correlating number
                                int counter = 1;
                                foreach (AIPrediction prediction in validPredictions) 
                                {
                                    decimal confidence = Math.Round(prediction.Confidence, 0, MidpointRounding.AwayFromZero);
                                    labels.Add($"{counter}. {prediction.Label.FirstCharToUpper()} {confidence}%");
                                    counter++;
                                }
                            }
                        }
                        else
                        {
                            labels = validPredictions.Select(x => x.Label.FirstCharToUpper()).ToList();
                        }

                        //Send Notifications                  
                        await SendNotifications(camera, snapshotManager, labels);
                        _logger.LogInformation($"{id}: Valid object found in snapshot {snapshotCount} of {Config.MaxSnapshots} at EVENT TIME {overallStopwatch.ElapsedMilliseconds}ms.");

                        //Stop snapshot bucle iteration:
                        snapshotCount = -1;
                    }
                    else if (predictions.Count() > 0)
                    {
                        // We got predictions back from the AI, but nothing that should trigger an alert
                        _logger.LogInformation($"{id}: No valid objects at EVENT TIME {overallStopwatch.ElapsedMilliseconds}ms.");
                    }
                    else
                    {
                        // We didn't get any predictions whatsoever from the AI
                        _logger.LogInformation($"{id}: Nothing detected by the AI at EVENT TIME {overallStopwatch.ElapsedMilliseconds}ms.");
                    }
                }
                snapshotCount++;
            }
            _logger.LogInformation($"{id}: FINISHED EVENT at EVENT TIME {overallStopwatch.ElapsedMilliseconds}ms.");
        }

        /// <summary>
        /// Handles any required preprocessing of the captured image.
        /// </summary>
        /// <param name="camera">The camera that the snapshot is from.</param>
        /// <param name="snapshot">The image data.</param>
        /// <returns>A byte array of the image.</returns>
        private byte[] PreProcessSnapshot(Camera camera, byte[] snapshot)
        {
            if (camera.Rotate != 0)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();

                // Load the bitmap & rotate the image
                //SKBitmap bitmap = SKBitmap.Decode(new MemoryStream(snapshot));
                SKBitmap bitmap = SKBitmap.Decode(snapshot);

                _logger.LogInformation($"{camera.Name}: Rotating image {camera.Rotate} degrees.");
                bitmap = Rotate(bitmap, camera.Rotate);

                using (SKPixmap pixmap = bitmap.PeekPixels())
                using (SKData data = pixmap.Encode(SKEncodedImageFormat.Jpeg, 100)) 
                { 
                    _logger.LogInformation($"{camera.Name}: Image preprocessing complete ({stopwatch.ElapsedMilliseconds}ms).");
                    return data.ToArray();
                }
            }
            else 
            {
                return snapshot;
            }
        }

        /// <summary>
        /// Rotates the image to the specified angle.
        /// </summary>
        /// <param name="bitmap">The bitmap to rotate.</param>
        /// <param name="angle">The angle to rotate to.</param>
        /// <returns>The rotated bitmap.</returns>
        private SKBitmap Rotate(SKBitmap bitmap, double angle)
        {
            double radians = Math.PI * angle / 180;
            float sine = (float)Math.Abs(Math.Sin(radians));
            float cosine = (float)Math.Abs(Math.Cos(radians));
            int originalWidth = bitmap.Width;
            int originalHeight = bitmap.Height;
            int rotatedWidth = (int)(cosine * originalWidth + sine * originalHeight);
            int rotatedHeight = (int)(cosine * originalHeight + sine * originalWidth);

            SKBitmap rotatedBitmap = new SKBitmap(rotatedWidth, rotatedHeight);
            using (SKCanvas canvas = new SKCanvas(rotatedBitmap))
            {
                canvas.Clear();
                canvas.Translate(rotatedWidth / 2, rotatedHeight / 2);
                canvas.RotateDegrees((float)angle);
                canvas.Translate(-originalWidth / 2, -originalHeight / 2);
                canvas.DrawBitmap(bitmap, new SKPoint());
            }
            
            return rotatedBitmap;
        }

        private async Task SendNotifications(Camera camera, ISnapshotManager snapshotManager, IList<String> labels)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            IEnumerable<INotifier> notifiers = Config.Notifiers.Where(x=> x.Cameras == null || x.Cameras.Count() == 0 ||
                x.Cameras.Any(c=> c.Equals(camera.Name, StringComparison.OrdinalIgnoreCase))).ToList();

            List<Task> tasks = new List<Task>();
            foreach (INotifier notifier in notifiers)
            {
                tasks.Add(notifier.SendAsync(camera, snapshotManager, labels, _logger));
            }

            await Task.WhenAll(tasks);

            _logger.LogInformation($"{camera.Name}: Notifications sent ({stopwatch.ElapsedMilliseconds}ms).");
        }
        
        /// <summary>
        /// Gets an image snapshot (in memory) from Surveillation Station.
        /// </summary>
        /// <param name="cameraName">The name of the camera to get the snapshot for.</param>
        /// <returns>A byte array for the image, or null on failure.</returns>
        private async Task<byte[]> GetSnapshot(string cameraName)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            byte[] imageBytes = await _synologyService.TakeSnapshotAsync(cameraName);
            if (imageBytes == null)
            {
                _logger.LogError($"{cameraName}: Failed to get snapshot.");
                return null;
            }
            else
            {
                stopwatch.Stop();
                _logger.LogInformation($"{cameraName}: Snapshot received in {stopwatch.ElapsedMilliseconds}ms.");
            }

            return imageBytes;
        }

        /// <summary>
        /// Passes the provided image to the AI and gets the predictions back.
        /// </summary>
        /// <param name="camera">The camera that the image is from.</param>
        /// <param name="imageBytes">The in-memory image for processing.</param>
        /// <returns>A list of predictions, or null on failure.</returns>
        private async Task<IEnumerable<AIPrediction>> GetAIPredications(Camera camera, byte[] imageBytes)
        {
            IEnumerable<AIPrediction> predictions = await _aiService.ProcessAsync(camera, imageBytes);
            if (predictions == null)
            {
                _logger.LogError($"{camera}: Failed to get get predictions.");
            }
            else if (_logger.IsEnabled(LogLevel.Information))
            {
                foreach (AIPrediction prediction in predictions)
                {
                    _logger.LogInformation($"{camera}: {prediction.Label} ({prediction.Confidence}%) [Size: {prediction.SizeX}x{prediction.SizeY}] [Start: {prediction.MinX},{prediction.MinY} | End: {prediction.MaxX},{prediction.MaxY}]");
                }
            }
            return predictions;
        }
    }
}
