﻿using System;
using System.Linq;
using System.Threading.Tasks;
using DiceMastersDiscordBot.Entities;
using DiceMastersDiscordBot.Properties;
using DiceMastersDiscordBot.Services;
using Microsoft.Extensions.Logging;

namespace DiceMastersDiscordBot.Events
{
    public class StandaloneChallongeEvent : BaseDiceMastersEvent
    {
        private string _challongeTournamentName;
        public StandaloneChallongeEvent(ILoggerFactory loggerFactory,
                            IAppSettings appSettings,
                            DMSheetService dMSheetService,
                            ChallongeEvent challonge) : base(loggerFactory, appSettings, dMSheetService, challonge)
        {
            _useChallonge = true;
        }

        public override void Initialize(EventManifest manifest)
        {
            if(!string.IsNullOrEmpty(manifest.ChallongeTournamentName))
            {
                _challongeTournamentName = manifest.ChallongeTournamentName;
            }
            base.Initialize(manifest);
        }

        public async override Task<string> MarkPlayerHereAsync(EventUserInput eventUserInput)
        {
            string response = $"There was an error checking in Discord User {eventUserInput.DiscordName} - please check in manually at Challonge.com";
            try
            {
                var userInfo = _sheetService.GetUserInfoFromDiscord(eventUserInput.DiscordName);

                if (string.IsNullOrEmpty(userInfo.ChallongeName))
                {
                    response = $"Cannot check in Discord user {eventUserInput.DiscordName} as there is no mapped Challonge ID. Please use `.challonge mychallongeusername` to tell the {_settings.GetBotName()} who you are in Challonge.";
                }
                else
                {
                    var participants = await _challonge.GetAllParticipantsAsync(_challongeTournamentName);
                    var player = participants.SingleOrDefault(p => p.ChallongeUsername == userInfo.ChallongeName);
                    if (player == null)
                    {
                        response = $"There was an error checking in Challonge User {userInfo.ChallongeName} - they were not returned as registered for this tournament.";
                    }
                    else
                    {
                        var resultParticipant = await _challonge.CheckInParticipantAsync(player.Id.ToString(), _challongeTournamentName);
                        if (resultParticipant.CheckedIn == true)
                        {
                            response = $"Success! Challonge User {userInfo.ChallongeName} (Discord: {userInfo.DiscordName}) is checked in for the event!";
                        }
                        else
                        {
                            response = $"There was an error checking in Challonge User {userInfo.ChallongeName} - please check in manually at Challonge.com";
                        }
                    }
                }
            }
            catch (Exception exc)
            {
                _logger.LogError(exc.Message);
            }
            return response;
        }
    }
}
