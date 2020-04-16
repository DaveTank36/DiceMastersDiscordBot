using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiceMastersDiscordBot.Entities;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiceMastersDiscordBot.Services
{
    public class DiscordBot : BackgroundService
    {
        private DiscordSocketClient _client;
        private CommandService _commands;


        private readonly DMSheetService _sheetService;

        private const string SUBMIT_STRING = "!submit";


        public DiscordBot(ILoggerFactory loggerFactory, IConfiguration config, DMSheetService dMSheetService)
        {
            Logger = loggerFactory.CreateLogger<DiscordBot>();
            Config = config;
            _sheetService = dMSheetService;
        }

        public ILogger Logger { get; }
        public IConfiguration Config { get; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logger.LogInformation("ServiceA is starting.");

            stoppingToken.Register(() => Logger.LogInformation("ServiceA is stopping."));

            try
            {
                _client = new DiscordSocketClient();

                _client.Log += Log;

                //Initialize command handling.
                _client.MessageReceived += DiscordMessageReceived;
                //await InstallCommands();      

                // Connect the bot to Discord
                string token = Config["DiscordToken"];
                await _client.LoginAsync(TokenType.Bot, token);
                await _client.StartAsync();

                // Block this task until the program is closed.
                //await Task.Delay(-1, stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    Logger.LogInformation("ServiceA is doing background work.");

                    _sheetService.CheckSheets();

                    await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                }
            } catch (Exception exc)
            {
                Logger.LogError(exc.Message);
            }

            Logger.LogInformation("ServiceA has stopped.");
        }

        private async Task DiscordMessageReceived(SocketMessage message)
        {
            if (message.Author.IsBot) return;
            var dmchannelID = await message.Author.GetOrCreateDMChannelAsync();
            if (message.Channel.Id == dmchannelID.Id)
            {
                if (message.Content.ToLower().StartsWith("submit") || message.Content.ToLower().StartsWith("!submit"))
                {
                    try
                    {
                        var eventName = "undetermined";
                        var args = message.Content.Split(' ');

                        switch (args[1].ToUpper())
                        {
                            case "WDA":
                            case "WEEKLY-DICE-ARENA":
                                eventName = Refs.EVENT_WDA;
                                break;
                            case "DF":
                            case "DICE-FIGHT":
                                eventName = Refs.EVENT_DICE_FIGHT;
                                break;
                            case "TOTM":
                            case "TEAM-OF-THE-MONTH":
                                eventName = Refs.EVENT_TOTM;
                                break;
                            default:
                                break;
                        }

                        await message.Channel.SendMessageAsync(SubmitTeamLink(eventName, args[2], message));
                    }
                    catch (Exception exc)
                    {
                        Console.WriteLine($"Exception submitting team via DM: {exc.Message}");
                        await message.Channel.SendMessageAsync("Sorry, I was unable to determine which event you wanted to submit to.");
                        await message.Channel.SendMessageAsync(Refs.DMBotSubmitTeamHint);
                        await message.Channel.SendMessageAsync("If you think you're doing it right and it still doesn't work, message Yort or post in #bot-uprising");
                    }
                }
                else if(message.Content.ToLower().StartsWith("!win"))
                {
                    try
                    {
                        var sheetsService = _sheetService.AuthorizeGoogleSheets();
                        string[] args = message.Content.Split(" ");
                        ColumnInput ci = new ColumnInput { Column1Value = message.Author.Username, Column2Value = args[1] };
                        // record it in the spreadsheet
                        await message.Channel.SendMessageAsync(
                            _sheetService.SendLinkToGoogle(sheetsService, message, _sheetService.DiceFightSheetId, "WINSheet", ci));
                        await message.Channel.SendMessageAsync("Also, I hope to do this automatically soon, but I am already up to late, so would you mind terribly re-submitting your team so your WIN name gets recorded properly? Thanks, I reaZZZZZZZZZ");
                        // update current DiceFight sheet?
                        
                    }
                    catch (Exception exc)
                    {
                        Console.WriteLine($"Exception in UpdateWinName: {exc.Message}");
                        await message.Channel.SendMessageAsync("Sorry, I was unable to record your WIN Name. Please contact Yort and tell him what went wrong");
                    }
                }
                else
                {
                    await message.Channel.SendMessageAsync("Sorry, I don't understand that command. I'm not actually that smart, you know.");
                }
            }
            if (message.Content == "!ping")
            {
                await message.Channel.SendMessageAsync("Pong!");
            }
            else if(message.Content.StartsWith(SUBMIT_STRING))
            {
                // first delete the original message
                await message.Channel.DeleteMessageAsync(message);
                var teamlink = message.Content.TrimStart(SUBMIT_STRING.ToCharArray()).Trim();
                await message.Channel.SendMessageAsync(SubmitTeamLink(message.Channel.Name, teamlink, message));
            }
            else if (message.Content.StartsWith("!format"))
            {
                await message.Channel.SendMessageAsync(GetCurrentFormat(message));
            }
            else if (message.Content.StartsWith("!count"))
            {
                await message.Channel.SendMessageAsync(_sheetService.GetCurrentPlayerCount(message));
            }
            else if (message.Content.StartsWith("!list"))
            {
                await message.Channel.SendMessageAsync(_sheetService.GetCurrentPlayerList(message));
            }
            else if (message.Content.StartsWith("!help"))
            {
                await message.Channel.SendMessageAsync(Refs.DMBotCommandHelpString);
            }
            else if (message.Content.StartsWith("!test"))
            {
                await message.Author.SendMessageAsync("Test response");
            }
        }



        private string GetCurrentFormat(SocketMessage message)
        {
            var sheetsService = _sheetService.AuthorizeGoogleSheets();

            if (message.Channel.Name == "weekly-dice-arena")
            {
                return _sheetService.GetFormatFromGoogle(sheetsService, message, _sheetService.WDASheetId, Refs.WdaSheetName);
            }
            else if (message.Channel.Name == "dice-fight")
            {
                try
                {
                    DiceFightHomeSheet s = _sheetService.GetDiceFightHomeSheet(sheetsService);
                    if(s ==  null) return "No information found for this week yet";

                    var nl = Environment.NewLine;
                    return $"Dice Fight for {s.EventDate}{nl}Format - {s.FormatDescription}{nl}Bans - {s.Bans}";
                }
                catch (Exception exc)
                {
                    Console.WriteLine($"Exception in Dice Fight: {exc.Message}");
                    return $"Ooops! Something went wrong with Dice Fight - please contact Yort (bot) or jacquesblondes (Dice Fight)";
                }
            }
            else if (message.Channel.Name == "team-of-the-month")
            {
                return _sheetService.GetFormatFromGoogle(sheetsService, message, _sheetService.TotMSheetId, Refs.TotMSheetName);
            }
            return "I can't accept team links on this channel";
        }

         private string SubmitTeamLink(string eventName, string teamlink, SocketMessage message)
        {
            var sheetsService = _sheetService.AuthorizeGoogleSheets();
            ColumnInput input;
            string response = Refs.DMBotSubmitTeamHint;
            if (eventName == "weekly-dice-arena")
            {
                input = new ColumnInput()
                {
                    Column1Value = message.Author.Username,
                    Column2Value = teamlink
                };
                response = _sheetService.SendLinkToGoogle(sheetsService, message, _sheetService.WDASheetId, Refs.WdaSheetName, input);
            }
            else if (eventName == "dice-fight")
            {
                string winName = _sheetService.GetWINName(sheetsService, _sheetService.DiceFightSheetId, message.Author.Username);
                DiceFightHomeSheet dfInfo = _sheetService.GetDiceFightHomeSheet(sheetsService);
                input = new ColumnInput()
                {
                    Column1Value = DateTime.Now.ToString(),
                    Column2Value = message.Author.Username,
                    Column3Value = teamlink,
                    Column4Value = winName
                };
                response = _sheetService.SendLinkToGoogle(sheetsService, message, _sheetService.DiceFightSheetId, dfInfo.SheetName, input);
                //SendDiceFightLinkToGoogle(sheetsService, message);
                if(string.IsNullOrWhiteSpace(winName))
                {
                    message.Author.SendMessageAsync(Refs.DiceFightAskForWin);
                }
            }
            else if(eventName == "team-of-the-month")
            {
                input = new ColumnInput()
                {
                    Column1Value = message.Author.Username,
                    Column2Value = teamlink
                };
                response = _sheetService.SendLinkToGoogle(sheetsService, message, _sheetService.TotMSheetId, Refs.TotMSheetName, input);
            }
            if( !response.Equals(Refs.DMBotSubmitTeamHint) )
            {
                message.Author.SendMessageAsync($"The following team was successfully submitted for {eventName}");
                message.Author.SendMessageAsync(teamlink);
            }
            return response;
        }




        //public async Task InstallCommands()
        //{
        //    _client.MessageReceived += CommandHandler;
        //    await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), null);
        //}

        private Task Log(LogMessage msg)
        {
            Logger.LogDebug(msg.ToString());
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }
    }
}
