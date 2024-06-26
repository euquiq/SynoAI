using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using SynoAI.Models;
using SynoAI.Extensions;

namespace SynoAI.Services
{
    /// <summary>
    /// A thread safe object for sharing file access between multiple notifications.
    /// </summary>
    public class SnapshotManager : ISnapshotManager
    {
        /// <summary>
        /// The bytes of the received snapshot.
        /// </summary>
        private readonly byte[] _snapshot;

        /// <summary>
        /// All the predictions for the AI; used when the draw mode is "All".
        /// </summary>
        private readonly IEnumerable<AIPrediction> _predictions;
        /// <summary>
        /// The valid predictions according to the camera configuration; used when the draw mode is "Match".
        /// </summary>
        private readonly IEnumerable<AIPrediction> _validPredictions;

        private ProcessedImage _processedImage;
        private object _processLock = new object();

        private readonly ILogger _logger;

        public SnapshotManager(byte[] snapshot, IEnumerable<AIPrediction> predictions, IEnumerable<AIPrediction> validPredictions, ILogger logger)
        {
            _snapshot = snapshot;
            _predictions = predictions;
            _validPredictions = validPredictions;
            _logger = logger;
        }

        /// <summary>
        /// Thread-safely processes the image by drawing image boundaries.
        /// </summary>
        /// <param name="camera">The camera the image came from.</param>
        public ProcessedImage GetImage(Camera camera)
        {
            if (_processedImage == null)
            {
                lock (_processLock)
                {
                    if (_processedImage == null)
                    {
                        // Save the image
                        string filePath;
                        using (SKBitmap image = ProcessImage(camera))
                        {
                            filePath = SaveImage(_logger, camera, image);
                        }

                        // Create the helper object
                        _processedImage = new ProcessedImage(filePath);
                    }
                }
            }
            return _processedImage;
        }

        /// <summary>
        /// Processes the source image by adding the boundary boxes and saves the file locally.
        /// </summary>
        /// <param name="camera">The camera the image came from.</param>
        /// <param name="imageBytes">The image data.</param>
        /// <param name="predictions">The list of predictions to add to the image.</param>
        private SKBitmap ProcessImage(Camera camera)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            _logger.LogInformation($"{camera.Name}: Processing image boundaries.");

            // Load the bitmap 
            SKBitmap image = SKBitmap.Decode(_snapshot);

            // Don't process the drawing if the drawing mode is off
            if (Config.DrawMode == DrawMode.Off)
            {
                _logger.LogInformation($"{camera.Name}: Draw mode is Off. Skipping image boundaries.");
                return image;
            }

            // Draw the predictions
            using (SKCanvas canvas = new SKCanvas(image))
            {
                int counter = 1; //used for assigning a reference number on each prediction if AlternativeLabelling is true

                foreach (AIPrediction prediction in Config.DrawMode == DrawMode.All ? _predictions : _validPredictions)
                {
                    // Write out anything detected that was above the minimum size
                    int minSizeX = camera.GetMinSizeX();
                    int minSizeY = camera.GetMinSizeY();
                    if (prediction.SizeX >= minSizeX && prediction.SizeY >= minSizeY)
                    {
                        // Draw the box
                        SKRect rectangle = SKRect.Create(prediction.MinX, prediction.MinY, prediction.SizeX, prediction.SizeY);
                        canvas.DrawRect(rectangle, new SKPaint 
                        {
                            Style = SKPaintStyle.Stroke,
                            Color = GetColour(Config.BoxColor)
                        });
                        
                        //Label creation, either classic label or alternative labelling (and only if there is more than one object)
                        string label = String.Empty;

                        if (Config.AlternativeLabelling && Config.DrawMode == DrawMode.Matches) 
                        {
                            //On alternatie labelling, just place a reference number and only if there is more than one object
                            if (_validPredictions.Count() > 1) 
                            {
                                label = counter.ToString();
                                counter++;
                            }
                        }
                        else
                        {
                            decimal confidence = Math.Round(prediction.Confidence, 0, MidpointRounding.AwayFromZero);
                            label = $"{prediction.Label.FirstCharToUpper()} {confidence}%";
                        }

                        //Label positioning
                        int x = prediction.MinX + Config.TextOffsetX;
                        int y = prediction.MinY + Config.FontSize + Config.TextOffsetY;

                        //Consider below box placement
                        if (Config.LabelBelowBox) 
                        {
                            y += prediction.SizeY;
                        }
      
                        // Draw the text
                        SKFont font = new SKFont(SKTypeface.FromFamilyName(Config.Font), Config.FontSize);
                        canvas.DrawText(label, x, y, font, new SKPaint 
                        {
                            Color = GetColour(Config.FontColor)
                        });
                    }
                }
            }

            stopwatch.Stop();
            _logger.LogInformation($"{camera.Name}: Finished processing image boundaries ({stopwatch.ElapsedMilliseconds}ms).");

            return image;
        }

        /// <summary>
        /// Saves the original unprocessed image from the provided byte array to the camera's capture directory.
        /// </summary>
        /// <param name="camera">The camera to save the image for.</param>
        /// <param name="image">The image to save.</param>
        public static string SaveOriginalImage(ILogger logger, Camera camera, byte[] snapshot)
        {
            SKBitmap image = SKBitmap.Decode(new MemoryStream(snapshot));
            return SaveImage(logger, camera, image, "Original");
        }

        /// <summary>
        /// Saves the image to the camera's capture directory.
        /// </summary>
        /// <param name="camera">The camera to save the image for.</param>
        /// <param name="image">The image to save.</param>
        private static string SaveImage(ILogger logger, Camera camera, SKBitmap image, string suffix = null)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            string directory = $"Captures";
            directory = Path.Combine(directory, camera.Name);

            if (!Directory.Exists(directory))
            {
                logger.LogInformation($"{camera}: Creating directory '{directory}'.");
                Directory.CreateDirectory(directory);
            }

            string fileName = $"{camera.Name}_{DateTime.Now:yyyy_MM_dd_HH_mm_ss_FFF}";
            if (!string.IsNullOrWhiteSpace(suffix))
            {
                fileName += "_" + suffix;
            }
            fileName+= ".jpeg";

            string filePath = Path.Combine(directory, fileName);
            logger.LogInformation($"{camera}: Saving image to '{filePath}'.");

            using (FileStream saveStream = new FileStream(filePath, FileMode.CreateNew))
            {
                bool saved = image.Encode(saveStream, SKEncodedImageFormat.Jpeg, 100);
                stopwatch.Stop();

                if (saved)
                {    
                    logger.LogInformation($"{camera}: Imaged saved to '{filePath}' ({stopwatch.ElapsedMilliseconds}ms).");
                }
                else
                {
                    logger.LogInformation($"{camera}: Failed to save image to '{filePath}' ({stopwatch.ElapsedMilliseconds}ms).");
                }
            }
            
            return filePath;
        }

        /// <summary>
        /// Parses the provided colour name into an SKColor.
        /// </summary>
        /// <param name="colour">The string to parse.</param>
        private SKColor GetColour(string hex)
        {
            if (!SKColor.TryParse(hex, out SKColor colour))
            {
                return SKColors.Red;
            }
            return colour;  
        }
    }
}