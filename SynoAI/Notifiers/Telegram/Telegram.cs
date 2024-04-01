using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Text;
using Newtonsoft.Json;
using SynoAI.Models;
using SynoAI.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace SynoAI.Notifiers.Telegram
{
    /// <summary>
    /// Calls a third party API.
    /// </summary>
    public class Telegram : NotifierBase
    {
        /// <summary>
        /// The ID of the chat to send notifications to
        /// </summary>
        public string ChatID { get; set; }
        /// <summary>
        /// The token used to authenticate to Telegram
        /// </summary>
        public string Token { get; set; }
        /// <summary>
        /// Photo base URL
        /// </summary>
        public string PhotoBaseURL { get; set; }

        /// <summary>
        /// Sends a message and an image using the Telegram API.
        /// </summary>
        /// <param name="camera">The camera that triggered the notification.</param>
        /// <param name="snapshotManager">A thread safe object for fetching the processed image.</param>
        /// <param name="foundTypes">The list of types that were found.</param>
        /// <param name="logger">A logger.</param>
        public override async Task SendAsync(Camera camera, ISnapshotManager snapshotManager, IList<string> foundTypes, ILogger logger)
        {
            using (logger.BeginScope($"Telegram '{ChatID}'"))
            {
                // Assign camera name to variable for logger placeholder
                string cameraName = camera.Name;

                try
                {
                    TelegramBotClient bot = new TelegramBotClient(Token);
                    ProcessedImage processedImage = snapshotManager.GetImage(camera);

                    string message = GetMessage(camera, foundTypes);
                    if (string.IsNullOrWhiteSpace(PhotoBaseURL))
                    {
                        // The photo base URL hasn't been specified, which means we need to send the file ourselves
                        using (FileStream fileStream = processedImage.GetReadonlyStream())
                        {
                            InputFile archivo = InputFile.FromStream(fileStream, processedImage.FileName);
                            await bot.SendPhotoAsync(ChatID, archivo, null, message);
                        }
                        // TODO - Add a config to disable the sending of the image?
                    }
                    else
                    {
                        string photoUrl = $"{PhotoBaseURL}/{camera.Name}/{processedImage.FileName}";
                        InputFile archivo = InputFile.FromString(photoUrl);
                        await bot.SendPhotoAsync(ChatID, archivo, null, message);
                    }

                    logger.LogInformation("{cameraName}: Telegram notification sent successfully", cameraName);
                } 
                catch (Exception ex)
                {
                    logger.LogError("{cameraName}: Error occurred sending telegram", cameraName);
                    logger.LogError(ex, "An exception occurred");
                }
            }
        }
    }
}
