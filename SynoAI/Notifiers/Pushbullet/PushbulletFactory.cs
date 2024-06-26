﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SynoAI.Notifiers.Pushbullet
{
    public class PushbulletFactory : NotifierFactory
    {
        public override INotifier Create(ILogger logger, IConfigurationSection section)
        {
            string apiKey = section.GetValue<string>("ApiKey");

            return new Pushbullet()
            {
                ApiKey = apiKey
            };
        }
    }
}
