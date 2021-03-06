﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Google.Apis.YouTube.v3.Data;

using Newtonsoft.Json;

using SpotifyAPI.Web.Models;

using TwitchBot.Configuration;
using TwitchBot.Enums;
using TwitchBot.Libraries;
using TwitchBot.Models;
using TwitchBot.Models.JSON;
using TwitchBot.Services;
using TwitchBot.Threads;

using TwitchBotDb.Models;
using TwitchBotDb.Temp;

using TwitchBotUtil.Extensions;

namespace TwitchBot.Commands
{
    public class CmdGen
    {
        private IrcClient _irc;
        private SpotifyWebClient _spotify;
        private TwitchBotConfigurationSection _botConfig;
        private int _broadcasterId;
        private TwitchInfoService _twitchInfo;
        private BankService _bank;
        private FollowerService _follower;
        private SongRequestBlacklistService _songRequestBlacklist;
        private ManualSongRequestService _manualSongRequest;
        private PartyUpService _partyUp;
        private GameDirectoryService _gameDirectory;
        private QuoteService _quote;
        private ErrorHandler _errHndlrInstance = ErrorHandler.Instance;
        private YoutubeClient _youTubeClientInstance = YoutubeClient.Instance;
        private BankHeistSingleton _heistSettingsInstance = BankHeistSingleton.Instance;
        private BossFightSingleton _bossSettingsInstance = BossFightSingleton.Instance;
        private TwitchChatterList _twitchChatterListInstance = TwitchChatterList.Instance;

        public CmdGen(IrcClient irc, SpotifyWebClient spotify, TwitchBotConfigurationSection botConfig, int broadcasterId,
            TwitchInfoService twitchInfo, BankService bank, FollowerService follower, SongRequestBlacklistService songRequestBlacklist,
            ManualSongRequestService manualSongRequest, PartyUpService partyUp, GameDirectoryService gameDirectory, QuoteService quote)
        {
            _irc = irc;
            _spotify = spotify;
            _botConfig = botConfig;
            _broadcasterId = broadcasterId;
            _twitchInfo = twitchInfo;
            _bank = bank;
            _follower = follower;
            _songRequestBlacklist = songRequestBlacklist;
            _manualSongRequest = manualSongRequest;
            _partyUp = partyUp;
            _gameDirectory = gameDirectory;
            _quote = quote;
        }

        public async void CmdDisplayCmds()
        {
            try
            {
                _irc.SendPublicChatMessage("---> !hello, !slap @[username], !stab @[username], !throw [item] @[username], !shoot @[username], "
                    + "!sr [youtube link/search], !ytsl, !partyup [party member name], !gamble [money], !join, "
                    + "!quote, !8ball [question], !" + _botConfig.CurrencyType.ToLower() + " (check stream currency) <---"
                    + " Link to full list of commands: http://bit.ly/2bXLlEe");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdCmds()", false, "!cmds");
            }
        }

        public async void CmdHello(TwitchChatter chatter)
        {
            try
            {
                _irc.SendPublicChatMessage($"Hey @{chatter.Username}! Thanks for talking to me :) " 
                    + $"I'll let @{_botConfig.Broadcaster.ToLower()} know you're here!");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdHello(string)", false, "!hello");
            }
        }

        public async void CmdUtcTime()
        {
            try
            {
                _irc.SendPublicChatMessage($"UTC Time: {DateTime.UtcNow.ToString()}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdUtcTime()", false, "!utctime");
            }
        }

        public async void CmdHostTime()
        {
            try
            {
                string response = $"{_botConfig.Broadcaster}'s Current Time: {DateTime.Now.ToString()} ";

                if (DateTime.Now.IsDaylightSavingTime())
                    response += $"({TimeZone.CurrentTimeZone.DaylightName})";
                else
                    response += $"({TimeZone.CurrentTimeZone.StandardName})";

                _irc.SendPublicChatMessage(response);
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdHostTime()", false, "!hosttime");
            }
        }

        public async Task CmdUptime()
        {
            try
            {
                RootStreamJSON streamJson = await _twitchInfo.GetBroadcasterStream();

                // Check if the channel is live
                if (streamJson.Stream != null)
                {
                    string duration = streamJson.Stream.CreatedAt;
                    TimeSpan ts = DateTime.UtcNow - DateTime.Parse(duration, new DateTimeFormatInfo(), DateTimeStyles.AdjustToUniversal);
                    string strResultDuration = String.Format("{0:h\\:mm\\:ss}", ts);
                    _irc.SendPublicChatMessage("This channel's current uptime (length of current stream) is " + strResultDuration);
                }
                else
                    _irc.SendPublicChatMessage("This channel is not streaming right now");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdUptime()", false, "!uptime");
            }
        }

        /// <summary>
        /// Display list of requested songs
        /// </summary>
        /// <param name="isManualSongRequestAvail">Check if song requests are available</param>
        /// <param name="chatter">User that sent the message</param>
        public async Task CmdManualSrList(bool isManualSongRequestAvail, TwitchChatter chatter)
        {
            try
            {
                if (!isManualSongRequestAvail)
                    _irc.SendPublicChatMessage($"Song requests are not available at this time @{chatter.Username}");
                else
                    _irc.SendPublicChatMessage(await _manualSongRequest.ListSongRequests(_broadcasterId));
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdManualSrList(bool, TwitchChatter)", false, "!rbsrl");
            }
        }

        /// <summary>
        /// Displays link to the list of songs that can be requested manually
        /// </summary>
        /// <param name="isManualSongRequestAvail">Check if song requests are available</param>
        /// <param name="chatter">User that sent the message</param>
        public async void CmdManualSrLink(bool isManualSongRequestAvail, TwitchChatter chatter)
        {
            try
            {
                if (!isManualSongRequestAvail)
                    _irc.SendPublicChatMessage($"Song requests are not available at this time @{chatter.Username}");
                else
                    _irc.SendPublicChatMessage($"Here is the link to the songs you can manually request {_botConfig.ManualSongRequestLink}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdManualSrLink(bool, TwitchChatter)", false, "!rbsl");
            }
        }

        /// <summary>
        /// Request a song for the host to play
        /// </summary>
        /// <param name="isSongRequestAvail">Check if song request system is enabled</param>
        /// <param name="chatter">User that sent the message</param>
        public async Task CmdManualSr(bool isSongRequestAvail, TwitchChatter chatter)
        {
            try
            {
                // Check if song request system is enabled
                if (isSongRequestAvail)
                {
                    // Grab the song name from the request
                    int index = chatter.Message.IndexOf(" ");
                    string songRequest = chatter.Message.Substring(index + 1);

                    // Check if song request has more than allowed symbols
                    if (!Regex.IsMatch(songRequest, @"^[a-zA-Z0-9 \-\(\)\'\?\,\/\""]+$"))
                    {
                        _irc.SendPublicChatMessage("Only letters, numbers, commas, hyphens, parentheses, "
                            + "apostrophes, forward-slash, and question marks are allowed. Please try again. "
                            + "If the problem persists, please contact my creator");
                    }
                    else
                    {
                        await _manualSongRequest.AddSongRequest(songRequest, chatter.Username, _broadcasterId);

                        _irc.SendPublicChatMessage($"The song \"{songRequest}\" has been successfully requested @{chatter.Username}");
                    }
                }
                else
                    _irc.SendPublicChatMessage($"Song requests are disabled at the moment @{chatter.Username}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdManualSr(bool, TwitchChatter)", false, "!rbsr", chatter.Message);
            }
        }

        /// <summary>
        /// Displays the current song being played from Spotify
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        public async Task CmdSpotifyCurrentSong(TwitchChatter chatter)
        {
            try
            {
                PlaybackContext playbackContext = await _spotify.GetPlayback();
                if (playbackContext != null && playbackContext.IsPlaying)
                {
                    string artistName = "";

                    foreach (SimpleArtist simpleArtist in playbackContext.Item.Artists)
                    {
                        artistName += $"{simpleArtist.Name}, ";
                    }

                    artistName = artistName.ReplaceLastOccurrence(", ", "");

                    TimeSpan timeSpan = TimeSpan.FromMilliseconds(playbackContext.Item.DurationMs);

                    _irc.SendPublicChatMessage($"Now Playing: \"{playbackContext.Item.Name}\" by {artistName} " 
                        + $"({Math.Floor(timeSpan.TotalMinutes)}M{timeSpan.Seconds}S)");
                }
                else
                    _irc.SendPublicChatMessage($"Nothing is playing at the moment @{chatter.Username}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdSpotifyCurrentSong(TwitchChatter)", false, "!spotifysong");
            }
        }

        /// <summary>
        /// Slaps a user and rates its effectiveness
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        public async Task<DateTime> CmdSlap(TwitchChatter chatter)
        {
            try
            {
                string recipient = chatter.Message.Substring(chatter.Message.IndexOf("@") + 1).ToLower();
                ReactionCmd(chatter.Username, recipient, "Stop smacking yourself", "slaps", Effectiveness());
                return DateTime.Now.AddSeconds(20);
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdSlap(TwitchChatter)", false, "!slap", chatter.Message);
            }

            return DateTime.Now;
        }

        /// <summary>
        /// Stabs a user and rates its effectiveness
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        public async Task<DateTime> CmdStab(TwitchChatter chatter)
        {
            try
            {
                string recipient = chatter.Message.Substring(chatter.Message.IndexOf("@") + 1).ToLower();
                ReactionCmd(chatter.Username, recipient, "Stop stabbing yourself! You'll bleed out", "stabs", Effectiveness());
                return DateTime.Now.AddSeconds(20);
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdStab(TwitchChatter)", false, "!stab", chatter.Message);
            }

            return DateTime.Now;
        }

        /// <summary>
        /// Shoots a viewer's random body part
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        public async Task<DateTime> CmdShoot(TwitchChatter chatter)
        {
            try
            {
                string bodyPart = "'s ";
                string recipient = chatter.Message.Substring(chatter.Message.IndexOf("@") + 1).ToLower();
                Random rnd = new Random(DateTime.Now.Millisecond);
                int bodyPartId = rnd.Next(8); // between 0 and 7

                if (bodyPartId == 0)
                    bodyPart += "head";
                else if (bodyPartId == 1)
                    bodyPart += "left leg";
                else if (bodyPartId == 2)
                    bodyPart += "right leg";
                else if (bodyPartId == 3)
                    bodyPart += "left arm";
                else if (bodyPartId == 4)
                    bodyPart += "right arm";
                else if (bodyPartId == 5)
                    bodyPart += "stomach";
                else if (bodyPartId == 6)
                    bodyPart += "neck";
                else // found largest random value
                    bodyPart = " but missed";

                if (bodyPart.Equals(" but missed"))
                {
                    _irc.SendPublicChatMessage($"Ha! You missed @{chatter.Username}");
                }
                else
                {
                    // bot makes a special response if shot at
                    if (recipient.Equals(_botConfig.BotName.ToLower()))
                    {
                        _irc.SendPublicChatMessage($"You think shooting me in the {bodyPart.Replace("'s ", "")} would hurt me? I am a bot!");
                    }
                    else // viewer is the target
                    {
                        ReactionCmd(chatter.Username, recipient, $"You just shot your own {bodyPart.Replace("'s ", "")}", "shoots", bodyPart);
                        return DateTime.Now.AddSeconds(20);
                    }
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdShoot(TwitchChatter)", false, "!shoot", chatter.Message);
            }

            return DateTime.Now;
        }

        /// <summary>
        /// Throws an item at a viewer and rates its effectiveness against the victim
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        public async Task<DateTime> CmdThrow(TwitchChatter chatter)
        {
            try
            {
                int indexAction = chatter.Message.IndexOf(" ");

                if (chatter.Message.StartsWith("!throw @"))
                    _irc.SendPublicChatMessage($"Please throw an item to a user @{chatter.Username}");
                else
                {
                    string recipient = chatter.Message.Substring(chatter.Message.IndexOf("@") + 1).ToLower();
                    string item = chatter.Message.Substring(indexAction, chatter.Message.IndexOf("@") - indexAction - 1);

                    ReactionCmd(chatter.Username, recipient, $"Stop throwing {item} at yourself", $"throws {item} at", $". {Effectiveness()}");
                    return DateTime.Now.AddSeconds(20);
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdThrow(TwitchChatter)", false, "!throw", chatter.Message);
            }

            return DateTime.Now;
        }

        /// <summary>
        /// Request party member if game and character exists in party up system
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        public async Task CmdPartyUp(TwitchChatter chatter)
        {
            try
            {
                int inputIndex = chatter.Message.IndexOf(" ") + 1;

                // check if user entered something
                if (chatter.Message.Length < inputIndex)
                {
                    _irc.SendPublicChatMessage($"Please enter a party member @{chatter.Username}");
                    return;
                }

                // get current game info
                ChannelJSON json = await _twitchInfo.GetBroadcasterChannelById();
                string gameTitle = json.Game;
                string partyMemberName = chatter.Message.Substring(inputIndex);
                TwitchGameCategory game = await _gameDirectory.GetGameId(gameTitle);

                // attempt to add requested party member into the queue
                if (string.IsNullOrEmpty(gameTitle))
                {
                    _irc.SendPublicChatMessage("I cannot see the name of the game. It's currently set to either NULL or EMPTY. "
                        + "Please have the chat verify that the game has been set for this stream. "
                        + $"If the error persists, please have @{_botConfig.Broadcaster.ToLower()} retype the game in their Twitch Live Dashboard. "
                        + "If this error shows up again and your chat can see the game set for the stream, please contact my master with !support in this chat");
                    return;
                }
                else if (game == null || game.Id == 0)
                {
                    _irc.SendPublicChatMessage("This game is not part of the \"Party Up\" system");
                    return;
                }

                PartyUp partyMember = await _partyUp.GetPartyMember(partyMemberName, game.Id, _broadcasterId);

                if (partyMember == null)
                {
                    _irc.SendPublicChatMessage($"I couldn't find the requested party member \"{partyMemberName}\" @{chatter.Username}. "
                        + "Please check with the broadcaster for possible spelling errors");
                    return;
                }

                if (await _partyUp.HasUserAlreadyRequested(chatter.Username, game.Id, _broadcasterId))
                {
                    _irc.SendPublicChatMessage($"You have already requested a party member. "
                        + $"Please wait until your request has been completed @{chatter.Username}");
                    return;
                }

                await _partyUp.AddPartyMember(chatter.Username, partyMember.Id);

                _irc.SendPublicChatMessage($"@{chatter.Username}: {partyMemberName} has been added to the party queue");

            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdPartyUp(TwitchChatter)", false, "!partyup", chatter.Message);
            }
        }

        /// <summary>
        /// Check what other user's have requested
        /// </summary>
        public async Task CmdPartyUpRequestList()
        {
            try
            {
                // get current game info
                ChannelJSON json = await _twitchInfo.GetBroadcasterChannelById();
                string gameTitle = json.Game;
                TwitchGameCategory game = await _gameDirectory.GetGameId(gameTitle);

                if (string.IsNullOrEmpty(gameTitle))
                {
                    _irc.SendPublicChatMessage("I cannot see the name of the game. It's currently set to either NULL or EMPTY. "
                        + "Please have the chat verify that the game has been set for this stream. "
                        + $"If the error persists, please have @{_botConfig.Broadcaster.ToLower()} retype the game in their Twitch Live Dashboard. "
                        + "If this error shows up again and your chat can see the game set for the stream, please contact my master with !support in this chat");
                }
                else if (game == null || game.Id == 0)
                    _irc.SendPublicChatMessage("This game is currently not a part of the \"Party Up\" system");
                else
                    _irc.SendPublicChatMessage(await _partyUp.GetRequestList(game.Id, _broadcasterId));
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdPartyUpRequestList()", false, "!partyuprequestlist");
            }
        }

        /// <summary>
        /// Check what party members are available (if game is part of the party up system)
        /// </summary>
        public async Task CmdPartyUpList()
        {
            try
            {
                // get current game info
                ChannelJSON json = await _twitchInfo.GetBroadcasterChannelById();
                string gameTitle = json.Game;
                TwitchGameCategory game = await _gameDirectory.GetGameId(gameTitle);

                if (string.IsNullOrEmpty(gameTitle))
                {
                    _irc.SendPublicChatMessage("I cannot see the name of the game. It's currently set to either NULL or EMPTY. "
                        + "Please have the chat verify that the game has been set for this stream. "
                        + $"If the error persists, please have @{_botConfig.Broadcaster.ToLower()} retype the game in their Twitch Live Dashboard. "
                        + "If this error shows up again and your chat can see the game set for the stream, please contact my master with !support in this chat");
                    return;
                }
                else if (game == null || game.Id == 0)
                    _irc.SendPublicChatMessage("This game is currently not a part of the \"Party Up\" system");
                else
                    _irc.SendPublicChatMessage(await _partyUp.GetPartyList(game.Id, _broadcasterId));
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdPartyUpList()", false, "!partyuplist");
            }
        }

        /// <summary>
        /// Check user's account balance
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        public async Task CmdCheckFunds(TwitchChatter chatter)
        {
            try
            {
                int balance = await _bank.CheckBalance(chatter.Username, _broadcasterId);

                if (balance == -1)
                    _irc.SendPublicChatMessage($"You are not currently banking with us at the moment. Please talk to a moderator about acquiring {_botConfig.CurrencyType}");
                else
                    _irc.SendPublicChatMessage($"@{chatter.Username} currently has {balance.ToString()} {_botConfig.CurrencyType}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdCheckFunds(TwitchChatter)", false, "![currency name]");
            }
        }

        /// <summary>
        /// Gamble away currency
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        public async Task<DateTime> CmdGamble(TwitchChatter chatter)
        {
            try
            {
                int gambledMoney = 0; // Money put into the gambling system
                bool isValidMsg = false;
                string gambleMessage = chatter.Message.Substring(chatter.Message.IndexOf(" ") + 1);

                // Check if user wants to gamble all of their wallet
                // Else check if their message is a valid amount to gamble
                isValidMsg = gambleMessage.Equals("all", StringComparison.CurrentCultureIgnoreCase) ? true : int.TryParse(gambleMessage, out gambledMoney);

                if (!isValidMsg)
                {
                    _irc.SendPublicChatMessage($"Please insert a positive whole amount (no decimal numbers) to gamble @{chatter.Username}");
                    return DateTime.Now;
                }

                int walletBalance = await _bank.CheckBalance(chatter.Username, _broadcasterId);

                // Check if user wants to gamble all of their wallet
                if (gambleMessage.Equals("all", StringComparison.CurrentCultureIgnoreCase))
                {
                    gambledMoney = walletBalance;
                }

                if (gambledMoney < 1)
                    _irc.SendPublicChatMessage($"Please insert a positive whole amount (no decimal numbers) to gamble @{chatter.Username}");
                else if (gambledMoney > walletBalance)
                    _irc.SendPublicChatMessage($"You do not have the sufficient funds to gamble {gambledMoney} {_botConfig.CurrencyType} @{chatter.Username}");
                else
                {
                    Random rnd = new Random(DateTime.Now.Millisecond);
                    int diceRoll = rnd.Next(1, 101); // between 1 and 100
                    int newBalance = 0;

                    string result = $"@{chatter.Username} gambled ";
                    string allResponse = "";

                    if (gambledMoney == walletBalance)
                    {
                        allResponse = "ALL ";
                    }

                    result += $"{allResponse} {gambledMoney} {_botConfig.CurrencyType} and the dice roll was {diceRoll}. They ";

                    // Check the 100-sided die roll result
                    if (diceRoll < 61) // lose gambled money
                    {
                        newBalance = walletBalance - gambledMoney;
                        
                        result += $"lost {allResponse} {gambledMoney} {_botConfig.CurrencyType}";
                    }
                    else if (diceRoll >= 61 && diceRoll <= 98) // earn double
                    {
                        walletBalance -= gambledMoney; // put money into the gambling pot (remove money from wallet)
                        newBalance = walletBalance + (gambledMoney * 2); // recieve 2x earnings back into wallet
                        
                        result += $"won {gambledMoney * 2} {_botConfig.CurrencyType}";
                    }
                    else if (diceRoll == 99 || diceRoll == 100) // earn triple
                    {
                        walletBalance -= gambledMoney; // put money into the gambling pot (remove money from wallet)
                        newBalance = walletBalance + (gambledMoney * 3); // recieve 3x earnings back into wallet
                        
                        result += $"won {gambledMoney * 3} {_botConfig.CurrencyType}";
                    }

                    await _bank.UpdateFunds(chatter.Username, _broadcasterId, newBalance);

                    // Show how much the user has left if they didn't gamble all of their currency or gambled all and lost
                    if (allResponse != "ALL " || (allResponse == "ALL " && diceRoll < 61))
                    {
                        string possession = "has";

                        if (newBalance > 1)
                            possession = "have";

                        result += $" and now {possession} {newBalance} {_botConfig.CurrencyType}";
                    }

                    _irc.SendPublicChatMessage(result);
                    return DateTime.Now.AddSeconds(20);
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdGamble(string, string)", false, "!gamble", chatter.Message);
            }

            return DateTime.Now;
        }

        /// <summary>
        /// Display random broadcaster quote
        /// </summary>
        public async Task<DateTime> CmdQuote()
        {
            try
            {
                List<Quote> quotes = await _quote.GetQuotes(_broadcasterId);

                // Check if there any quotes inside the system
                if (quotes == null || quotes.Count == 0)
                    _irc.SendPublicChatMessage("There are no quotes to be displayed at the moment");
                else
                {
                    // Randomly pick a quote from the list to display
                    Random rnd = new Random(DateTime.Now.Millisecond);
                    int index = rnd.Next(quotes.Count);

                    Quote resultingQuote = new Quote();
                    resultingQuote = quotes.ElementAt(index); // grab random quote from list of quotes
                    string quoteResult = $"\"{resultingQuote.UserQuote}\" - {_botConfig.Broadcaster} "
                        + $"({resultingQuote.TimeCreated.ToString("MMMM", CultureInfo.InvariantCulture)} {resultingQuote.TimeCreated.Year}) "
                        + $"< Quoted by @{resultingQuote.Username} >";

                    _irc.SendPublicChatMessage(quoteResult);
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdQuote()", false, "!quote");
            }

            return DateTime.Now.AddSeconds(20);
        }

        /// <summary>
        /// Tell the user how long they have been following the broadcaster
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        /// <returns></returns>
        public async Task CmdFollowSince(TwitchChatter chatter)
        {
            try
            {
                if (chatter.Username == _botConfig.Broadcaster.ToLower())
                {
                    _irc.SendPublicChatMessage($"Please don't tell me you're really following yourself...are you {_botConfig.Broadcaster.ToLower()}? WutFace");
                    return;
                }

                chatter.CreatedAt = _twitchChatterListInstance.TwitchFollowers.FirstOrDefault(c => c.Username == chatter.Username).CreatedAt;

                if (chatter.CreatedAt == null)
                {
                    // get chatter info manually
                    RootUserJSON rootUserJSON = await _twitchInfo.GetUsersByLoginName(chatter.Username);

                    using (HttpResponseMessage message = await _twitchInfo.CheckFollowerStatus(rootUserJSON.Users.First().Id))
                    {
                        string body = await message.Content.ReadAsStringAsync();
                        FollowerJSON response = JsonConvert.DeserializeObject<FollowerJSON>(body);

                        if (!string.IsNullOrEmpty(response.CreatedAt))
                        {
                            chatter.CreatedAt = Convert.ToDateTime(response.CreatedAt);
                        }
                    }
                }

                // mainly used if chatter was originally null
                if (chatter.CreatedAt != null)
                {
                    DateTime startedFollowing = Convert.ToDateTime(chatter.CreatedAt);
                    _irc.SendPublicChatMessage($"@{chatter.Username} has been following since {startedFollowing.ToLongDateString()}");
                }
                else
                {
                    _irc.SendPublicChatMessage($"{chatter.Username} is not following {_botConfig.Broadcaster.ToLower()}");
                }

            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdFollowSince(TwitchChatter)", false, "!followsince");
            }
        }

        /// <summary>
        /// Display the follower's stream rank
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        /// <returns></returns>
        public async Task CmdViewRank(TwitchChatter chatter)
        {
            try
            {
                if (chatter.Username == _botConfig.Broadcaster.ToLower())
                {
                    _irc.SendPublicChatMessage($"Here goes {_botConfig.Broadcaster.ToLower()} flexing his rank...oh wait OpieOP");
                    return;
                }

                chatter.CreatedAt = _twitchChatterListInstance.TwitchFollowers.FirstOrDefault(c => c.Username == chatter.Username).CreatedAt;

                if (chatter.CreatedAt == null)
                {
                    using (HttpResponseMessage message = await _twitchInfo.CheckFollowerStatus(chatter.TwitchId))
                    {
                        string body = await message.Content.ReadAsStringAsync();
                        FollowerJSON response = JsonConvert.DeserializeObject<FollowerJSON>(body);

                        if (!string.IsNullOrEmpty(response.CreatedAt))
                        {
                            chatter.CreatedAt = Convert.ToDateTime(response.CreatedAt);
                        }
                    }
                }

                if (chatter.CreatedAt != null)
                {
                    int currExp = await _follower.CurrentExp(chatter.Username, _broadcasterId);

                    // Grab the follower's associated rank
                    if (currExp > -1)
                    {
                        IEnumerable<Rank> rankList = await _follower.GetRankList(_broadcasterId);
                        Rank currFollowerRank = _follower.GetCurrentRank(rankList, currExp);
                        decimal hoursWatched = _follower.GetHoursWatched(currExp);

                        _irc.SendPublicChatMessage($"@{chatter.Username}: \"{currFollowerRank.Name}\" "
                            + $"{currExp}/{currFollowerRank.ExpCap} EXP ({hoursWatched} hours watched)");
                    }
                    else
                    {
                        await _follower.EnlistRecruit(chatter.Username, _broadcasterId);

                        _irc.SendPublicChatMessage($"Welcome to the army @{chatter.Username}. View your new rank using !rank");
                    }
                }
                else
                {
                    _irc.SendPublicChatMessage($"{chatter.Username} is not following {_botConfig.Broadcaster.ToLower()}");
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdViewRank(TwitchChatter)", false, "!rank");
            }
        }

        /// <summary>
        /// Uses the Google API to add YouTube videos to the broadcaster's specified request playlist
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        /// <param name="hasYouTubeAuth">Checks if broadcaster allowed this bot to post videos to the playlist</param>
        /// <param name="isYouTubeSongRequestAvail">Checks if users can request songs</param>
        /// <returns></returns>
        public async Task<DateTime> CmdYouTubeSongRequest(TwitchChatter chatter, bool hasYouTubeAuth, bool isYouTubeSongRequestAvail)
        {
            try
            {
                if (!hasYouTubeAuth)
                {
                    _irc.SendPublicChatMessage("YouTube song requests have not been set up");
                    return DateTime.Now;
                }

                if (!isYouTubeSongRequestAvail)
                {
                    _irc.SendPublicChatMessage("YouTube song requests are not turned on");
                    return DateTime.Now;
                }

                int funds = await _bank.CheckBalance(chatter.Username, _broadcasterId);
                int cost = 250; // ToDo: Set YTSR currency cost into settings

                if (funds < cost)
                {
                    _irc.SendPublicChatMessage($"You do not have enough {_botConfig.CurrencyType} to make a song request. "
                        + $"You currently have {funds} {_botConfig.CurrencyType} @{chatter.Username}");
                }
                else
                {
                    string videoId = "";
                    int spaceIndex = chatter.Message.IndexOf(" ");

                    // Parse video ID based on different types of requests
                    if (chatter.Message.Contains("?v=") || chatter.Message.Contains("&v=") || chatter.Message.Contains("youtu.be/")) // full or short URL
                    {
                        videoId = _youTubeClientInstance.GetYouTubeVideoId(chatter.Message);
                    }
                    else if (chatter.Message.Substring(spaceIndex + 1).Length == 11
                        && chatter.Message.Substring(spaceIndex + 1).IndexOf(" ") == -1
                        && Regex.Match(chatter.Message, @"[\w\-]").Success) // assume only video ID
                    {
                        videoId = chatter.Message.Substring(spaceIndex + 1);
                    }
                    else // search by keyword
                    {
                        string videoKeyword = chatter.Message.Substring(spaceIndex + 1);
                        videoId = await _youTubeClientInstance.SearchVideoByKeyword(videoKeyword);
                    }

                    // Confirm if video ID has been found and is a new song request
                    if (string.IsNullOrEmpty(videoId))
                    {
                        _irc.SendPublicChatMessage($"Couldn't find video ID for song request @{chatter.Username}");
                    }
                    else if (await _youTubeClientInstance.HasDuplicatePlaylistItem(_botConfig.YouTubeBroadcasterPlaylistId, videoId))
                    {
                        _irc.SendPublicChatMessage($"Song has already been requested @{chatter.Username}");
                    }
                    else
                    {
                        Video video = await _youTubeClientInstance.GetVideoById(videoId);

                        // Check if video's title and account match song request blacklist
                        List<SongRequestIgnore> blacklist = await _songRequestBlacklist.GetSongRequestIgnore(_broadcasterId);

                        if (blacklist.Count > 0)
                        {
                            // Check for artist-wide blacklist
                            if (blacklist.Any(
                                    b => (string.IsNullOrEmpty(b.Title)
                                            && video.Snippet.Title.Contains(b.Artist, StringComparison.CurrentCultureIgnoreCase))
                                        || (string.IsNullOrEmpty(b.Title)
                                            && video.Snippet.ChannelTitle.Contains(b.Artist, StringComparison.CurrentCultureIgnoreCase))
                                ))
                            {
                                _irc.SendPublicChatMessage($"I'm not allowing this artist/video to be queued on my master's behalf @{chatter.Username}");
                                return DateTime.Now;
                            }
                            // Check for song-specific blacklist
                            else if (blacklist.Any(
                                    b => (!string.IsNullOrEmpty(b.Title) && video.Snippet.Title.Contains(b.Artist, StringComparison.CurrentCultureIgnoreCase)
                                            && video.Snippet.Title.Contains(b.Title, StringComparison.CurrentCultureIgnoreCase)) // both song/artist in video title
                                        || (!string.IsNullOrEmpty(b.Title) && video.Snippet.ChannelTitle.Contains(b.Artist, StringComparison.CurrentCultureIgnoreCase)
                                            && video.Snippet.Title.Contains(b.Title, StringComparison.CurrentCultureIgnoreCase)) // song in title and artist in channel title
                                ))
                            {
                                _irc.SendPublicChatMessage($"I'm not allowing this song to be queued on my master's behalf @{chatter.Username}");
                                return DateTime.Now;
                            }
                        }

                        // Check if video is blocked in the broadcaster's country
                        CultureInfo cultureInfo = Thread.CurrentThread.CurrentCulture;
                        RegionInfo regionInfo = new RegionInfo(cultureInfo.Name);
                        var regionRestriction = video.ContentDetails.RegionRestriction;

                        if ((regionRestriction?.Allowed != null && !regionRestriction.Allowed.Contains(regionInfo.TwoLetterISORegionName))
                            || (regionRestriction?.Blocked != null && regionRestriction.Blocked.Contains(regionInfo.TwoLetterISORegionName)))
                        {
                            _irc.SendPublicChatMessage($"Your song request is blocked in this broadcaster's country. Please request a different song");
                            return DateTime.Now;
                        }

                        string videoDuration = video.ContentDetails.Duration;

                        // Check if time limit has been reached
                        // ToDo: Make bot setting for duration limit based on minutes (if set)
                        if (!videoDuration.Contains("PT") || videoDuration.Contains("H"))
                        {
                            _irc.SendPublicChatMessage($"Either couldn't find the video duration or it was way too long for the stream @{chatter.Username}");
                        }
                        else
                        {
                            int timeIndex = videoDuration.IndexOf("T") + 1;
                            string parsedDuration = videoDuration.Substring(timeIndex);
                            int minIndex = parsedDuration.IndexOf("M");

                            string videoMin = "0";
                            string videoSec = "0";
                            int videoMinLimit = 10;
                            int videoSecLimit = 0;

                            if (minIndex > 0)
                                videoMin = parsedDuration.Substring(0, minIndex);

                            if (parsedDuration.IndexOf("S") > 0)
                                videoSec = parsedDuration.Substring(minIndex + 1).TrimEnd('S');

                            if (Convert.ToInt32(videoMin) >= videoMinLimit && Convert.ToInt32(videoSec) >= videoSecLimit)
                            {
                                _irc.SendPublicChatMessage("Song request is longer than or equal to " 
                                    + $"{videoMinLimit} minute(s) and {videoSecLimit} second(s) @{chatter.Username}");
                            }
                            else
                            {
                                await _youTubeClientInstance.AddVideoToPlaylist(videoId, _botConfig.YouTubeBroadcasterPlaylistId, chatter.Username);
                                await _bank.UpdateFunds(chatter.Username, _broadcasterId, funds - cost);

                                _irc.SendPublicChatMessage($"@{chatter.Username} spent {cost} {_botConfig.CurrencyType} " + 
                                    $"and \"{video.Snippet.Title}\" by {video.Snippet.ChannelTitle} ({videoMin}M{videoSec}S) was successfully requested!");

                                // Return cooldown time by using one-third of the length of the video duration
                                TimeSpan totalTimeSpan = new TimeSpan(0, Convert.ToInt32(videoMin), Convert.ToInt32(videoSec));
                                TimeSpan oneThirdTimeSpan = new TimeSpan(totalTimeSpan.Ticks / 3);

                                return DateTime.Now.AddSeconds(oneThirdTimeSpan.TotalSeconds);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdYouTubeSongRequest(TwitchChatter, bool, bool)", false, "!ytsr");
            }

            return DateTime.Now;
        }

        /// <summary>
        /// Display's link to broadcaster's YouTube song request playlist
        /// </summary>
        /// <param name="hasYouTubeAuth">Checks if broadcaster allowed this bot to post videos to the playlist</param>
        /// <param name="isYouTubeSongRequestAvail">Checks if users can request songs</param>
        public async void CmdYouTubeSongRequestList(bool hasYouTubeAuth, bool isYouTubeSongRequestAvail)
        {
            try
            {
                if (hasYouTubeAuth && isYouTubeSongRequestAvail && !string.IsNullOrEmpty(_botConfig.YouTubeBroadcasterPlaylistId))
                {
                    _irc.SendPublicChatMessage($"{_botConfig.Broadcaster.ToLower()}'s song request list is at " +
                        "https://www.youtube.com/playlist?list=" + _botConfig.YouTubeBroadcasterPlaylistId);
                }
                else
                {
                    _irc.SendPublicChatMessage("There is no song request list at this time");
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdYouTubeSongRequestList(bool, bool)", false, "!ytsl");
            }
        }

        /// <summary>
        /// Displays MultiStream link so multiple streamers can be watched at once
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        /// <param name="multiStreamUsers">List of broadcasters that are a part of the link</param>
        public async void CmdMultiStreamLink(TwitchChatter chatter, List<string> multiStreamUsers)
        {
            try
            {
                if (multiStreamUsers.Count == 0)
                    _irc.SendPublicChatMessage($"MultiStream link is not set up @{chatter.Username}");
                else
                {
                    string multiStreamLink = "https://multitwitch.live/" + _botConfig.Broadcaster.ToLower();

                    foreach (string multiStreamUser in multiStreamUsers)
                        multiStreamLink += $"/{multiStreamUser}";

                    _irc.SendPublicChatMessage($"Check out these awesome streamers at the same time! (Use desktop for best results) {multiStreamLink}");
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdMultiStreamLink(TwitchChatter, List<string>)", false, "!msl");
            }
        }

        /// <summary>
        /// Ask any question and the Magic 8 Ball will give a fortune
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        public async Task<DateTime> CmdMagic8Ball(TwitchChatter chatter)
        {
            try
            {
                Random rnd = new Random(DateTime.Now.Millisecond);
                int answerId = rnd.Next(20); // between 0 and 19

                string[] possibleAnswers = new string[20]
                {
                    $"It is certain @{chatter.Username}",
                    $"It is decidedly so @{chatter.Username}",
                    $"Without a doubt @{chatter.Username}",
                    $"Yes definitely @{chatter.Username}",
                    $"You may rely on it @{chatter.Username}",
                    $"As I see it, yes @{chatter.Username}",
                    $"Most likely @{chatter.Username}",
                    $"Outlook good @{chatter.Username}",
                    $"Yes @{chatter.Username}",
                    $"Signs point to yes @{chatter.Username}",
                    $"Reply hazy try again @{chatter.Username}",
                    $"Ask again later @{chatter.Username}",
                    $"Better not tell you now @{chatter.Username}",
                    $"Cannot predict now @{chatter.Username}",
                    $"Concentrate and ask again @{chatter.Username}",
                    $"Don't count on it @{chatter.Username}",
                    $"My reply is no @{chatter.Username}",
                    $"My sources say no @{chatter.Username}",
                    $"Outlook not so good @{chatter.Username}",
                    $"Very doubtful @{chatter.Username}"
                };

                _irc.SendPublicChatMessage(possibleAnswers[answerId]);
                return DateTime.Now.AddSeconds(20);
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdMagic8Ball(TwitchChatter)", false, "!8ball");
            }

            return DateTime.Now;
        }

        /// <summary>
        /// Disply the top 3 richest users (if available)
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        public async Task CmdLeaderboardCurrency(TwitchChatter chatter)
        {
            try
            {
                List<Bank> richestUsers = await _bank.GetCurrencyLeaderboard(_botConfig.Broadcaster, _broadcasterId, _botConfig.BotName);

                if (richestUsers.Count == 0)
                {
                    _irc.SendPublicChatMessage($"Everyone's broke! @{chatter.Username} NotLikeThis");
                    return;
                }

                string resultMsg = "";
                foreach (Bank user in richestUsers)
                {
                    resultMsg += $"\"{user.Username}\" with {user.Wallet} {_botConfig.CurrencyType}, ";
                }

                resultMsg = resultMsg.Remove(resultMsg.Length - 2); // remove extra ","

                // improve list grammar
                if (richestUsers.Count == 2)
                    resultMsg = resultMsg.ReplaceLastOccurrence(", ", " and ");
                else if (richestUsers.Count > 2)
                    resultMsg = resultMsg.ReplaceLastOccurrence(", ", ", and ");

                if (richestUsers.Count == 1)
                    _irc.SendPublicChatMessage($"The richest user is {resultMsg}");
                else
                    _irc.SendPublicChatMessage($"The richest users are: {resultMsg}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdLeaderboardCurrency(TwitchChatter)", false, "![currency name]top3");
            }
        }

        /// <summary>
        /// Display the top 3 highest ranking members (if available)
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        public async Task CmdLeaderboardRank(TwitchChatter chatter)
        {
            try
            {
                IEnumerable<RankFollower> highestRankedFollowers = await _follower.GetFollowersLeaderboard(_botConfig.Broadcaster, _broadcasterId, _botConfig.BotName);

                if (highestRankedFollowers.Count() == 0)
                {
                    _irc.SendPublicChatMessage($"There's no one in your ranks. Start recruiting today! @{chatter.Username}");
                    return;
                }

                IEnumerable<Rank> rankList = await _follower.GetRankList(_broadcasterId);

                string resultMsg = "";
                foreach (RankFollower follower in highestRankedFollowers)
                {
                    Rank currFollowerRank = _follower.GetCurrentRank(rankList, follower.Experience);
                    decimal hoursWatched = _follower.GetHoursWatched(follower.Experience);

                    resultMsg += $"\"{currFollowerRank.Name} {follower.Username}\" with {hoursWatched} hour(s), ";
                }

                resultMsg = resultMsg.Remove(resultMsg.Length - 2); // remove extra ","

                // improve list grammar
                if (highestRankedFollowers.Count() == 2)
                    resultMsg = resultMsg.ReplaceLastOccurrence(", ", " and ");
                else if (highestRankedFollowers.Count() > 2)
                    resultMsg = resultMsg.ReplaceLastOccurrence(", ", ", and ");

                if (highestRankedFollowers.Count() == 1)
                    _irc.SendPublicChatMessage($"This leader's highest ranking member is {resultMsg}");
                else
                    _irc.SendPublicChatMessage($"This leader's highest ranking members are: {resultMsg}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdLeaderboardRank(TwitchChatter)", false, "!ranktop3");
            }
        }

        /// <summary>
        /// Play a friendly game of Russian Roulette and risk chat privileges for stream currency
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        public async Task<DateTime> CmdRussianRoulette(TwitchChatter chatter)
        {
            try
            {
                RouletteUser rouletteUser = Program.RouletteUsers.FirstOrDefault(u => u.Username == chatter.Username);

                Random rnd = new Random(DateTime.Now.Millisecond);
                int bullet = rnd.Next(6); // between 0 and 5

                if (bullet == 0) // user was shot
                {
                    if (rouletteUser != null)
                        Program.RouletteUsers.Remove(rouletteUser);

                    if (_botConfig.Broadcaster.ToLower() == chatter.Username || chatter.Badges.Contains("moderator"))
                    {
                        _irc.SendPublicChatMessage($"Enjoy your 15 minutes without russian roulette @{chatter.Username}");
                        return DateTime.Now.AddMinutes(15);
                    }

                    _irc.SendChatTimeout(chatter.Username, 300); // 5 minute timeout
                    _irc.SendPublicChatMessage($"You are dead @{chatter.Username}. Enjoy your 5 minutes in limbo (cannot talk)");
                    return DateTime.Now;
                }

                if (rouletteUser == null) // new roulette user
                {
                    rouletteUser = new RouletteUser() { Username = chatter.Username, ShotsTaken = 1 };
                    Program.RouletteUsers.Add(rouletteUser);

                    _irc.SendPublicChatMessage($"@{chatter.Username} -> 1/6 attempts");
                }
                else // existing roulette user
                {
                    if (rouletteUser.ShotsTaken < 6)
                    {
                        foreach (RouletteUser user in Program.RouletteUsers)
                        {
                            if (user.Username == chatter.Username)
                            {
                                user.ShotsTaken++;
                                break;
                            }
                        }
                    }

                    string responseMessage = $"@{chatter.Username} -> {rouletteUser.ShotsTaken}/6 attempts";

                    if (rouletteUser.ShotsTaken == 6)
                    {
                        int funds = await _bank.CheckBalance(chatter.Username, _broadcasterId);
                        int reward = 3000; // ToDo: Make roulette reward deposit config setting

                        if (funds > -1)
                        {
                            funds += reward; // deposit 500 stream currency
                            await _bank.UpdateFunds(chatter.Username, _broadcasterId, funds);
                        }
                        else
                            await _bank.CreateAccount(chatter.Username, _broadcasterId, reward);

                        Program.RouletteUsers.RemoveAll(u => u.Username == chatter.Username);

                        responseMessage = $"Congrats on surviving russian roulette. Here's {reward} {_botConfig.CurrencyType}!";

                        // Special cooldown for moderators/broadcasters after they win
                        if (_botConfig.Broadcaster.ToLower() == chatter.Username || chatter.Badges.Contains("moderator"))
                        {
                            _irc.SendPublicChatMessage(responseMessage);
                            return DateTime.Now.AddMinutes(5);
                        }
                    }

                    _irc.SendPublicChatMessage(responseMessage);
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdRussianRoulette(TwitchChatter)", false, "!roulette");
            }

            return DateTime.Now;
        }

        /// <summary>
        /// Show a list of users that are queued to play with the broadcaster
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        /// <param name="gameQueueUsers">List of users that are queued to play with the broadcaster</param>
        public async Task CmdListJoin(TwitchChatter chatter, Queue<string> gameQueueUsers)
        {
            try
            {
                if (!await IsMultiplayerGame(chatter.Username)) return;

                if (gameQueueUsers.Count == 0)
                {
                    _irc.SendPublicChatMessage($"No one wants to play with the streamer at the moment. "
                        + "Be the first to play with !join");
                    return;
                }

                // Show list of queued users
                string message = $"List of users waiting to play with the streamer (in order from left to right): < ";

                foreach (string user in gameQueueUsers)
                {
                    message += user + " >< ";
                }

                _irc.SendPublicChatMessage(message.Remove(message.Length - 2));
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdListJoin(TwitchChatter, Queue<string>)", false, "!listjoin");
            }
        }

        /// <summary>
        /// Add a user to the queue of users that want to play with the broadcaster
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        /// <param name="gameQueueUsers">List of users that are queued to play with the broadcaster</param>
        public async Task<Queue<string>> CmdJoin(TwitchChatter chatter, Queue<string> gameQueueUsers)
        {
            try
            {
                if (gameQueueUsers.Contains(chatter.Username))
                {
                    _irc.SendPublicChatMessage($"Don't worry @{chatter.Username}. You're on the list to play with " +
                        $"the streamer with your current position at {gameQueueUsers.ToList().IndexOf(chatter.Username) + 1} " +
                        $"of {gameQueueUsers.Count} user(s)");
                }
                else if (await IsMultiplayerGame(chatter.Username))
                {
                    gameQueueUsers.Enqueue(chatter.Username);

                    _irc.SendPublicChatMessage($"Congrats @{chatter.Username}! You're currently in line with your current position at " +
                        $"{gameQueueUsers.ToList().IndexOf(chatter.Username) + 1}");
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdJoin(TwitchChatter, Queue<string>)", false, "!join");
            }

            return gameQueueUsers;
        }

        /// <summary>
        /// Engage in the bank heist minigame
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        public async Task CmdBankHeist(TwitchChatter chatter)
        {
            try
            {
                BankHeist bankHeist = new BankHeist();
                int funds = await _bank.CheckBalance(chatter.Username, _broadcasterId);
                bool isValid = int.TryParse(chatter.Message.Substring(chatter.Message.IndexOf(" ")), out int gamble);

                if (_heistSettingsInstance.IsHeistOnCooldown())
                {
                    TimeSpan cooldown = _heistSettingsInstance.CooldownTimePeriod.Subtract(DateTime.Now);

                    if (cooldown.Minutes >= 1)
                    {
                        _irc.SendPublicChatMessage(_heistSettingsInstance.CooldownEntry
                            .Replace("@timeleft@", cooldown.Minutes.ToString()));
                    }
                    else
                    {
                        _irc.SendPublicChatMessage(_heistSettingsInstance.CooldownEntry
                            .Replace("@timeleft@", cooldown.Seconds.ToString())
                            .Replace("minutes", "seconds"));
                    }

                    return;
                }

                if (bankHeist.HasRobberAlreadyEntered(chatter.Username))
                {
                    _irc.SendPublicChatMessage($"You are already in this heist @{chatter.Username}");
                    return;
                }

                // check if funds and gambling amount are valid
                if (!isValid)
                {
                    _irc.SendPublicChatMessage($"Please gamble with a positive amount of {_botConfig.CurrencyType} @{chatter.Username}");
                    return;
                }
                else if (gamble < 1)
                {
                    _irc.SendPublicChatMessage($"You cannot gamble less than one {_botConfig.CurrencyType} @{chatter.Username}");
                    return;
                }
                else if (funds < 1)
                {
                    _irc.SendPublicChatMessage($"You need at least one {_botConfig.CurrencyType} to join the heist @{chatter.Username}");
                    return;
                }
                else if (funds < gamble)
                {
                    _irc.SendPublicChatMessage($"You do not have enough to gamble {gamble} {_botConfig.CurrencyType} @{chatter.Username}");
                    return;
                }
                else if (gamble > _heistSettingsInstance.MaxGamble)
                {
                    _irc.SendPublicChatMessage($"{_heistSettingsInstance.MaxGamble} {_botConfig.CurrencyType} is the most you can put in. " + 
                        $"Please try again with less {_botConfig.CurrencyType} @{chatter.Username}");
                    return;
                }
                
                if (!bankHeist.IsEntryPeriodOver())
                {
                    // make heist announcement if first robber and start recruiting members
                    if (_heistSettingsInstance.Robbers.Count == 0)
                    {
                        _heistSettingsInstance.EntryPeriod = DateTime.Now.AddSeconds(_heistSettingsInstance.EntryPeriodSeconds);
                        _irc.SendPublicChatMessage(_heistSettingsInstance.EntryMessage.Replace("user@", chatter.Username));
                    }

                    // join bank heist
                    BankRobber robber = new BankRobber { Username = chatter.Username, Gamble = gamble };
                    bankHeist.Produce(robber);
                    await _bank.UpdateFunds(chatter.Username, _broadcasterId, funds - gamble);

                    // display new heist level
                    if (!string.IsNullOrEmpty(bankHeist.NextLevelMessage()))
                    {
                        _irc.SendPublicChatMessage(bankHeist.NextLevelMessage());
                    }

                    // display if more than one robber joins
                    if (_heistSettingsInstance.Robbers.Count > 1)
                    {
                        _irc.SendPublicChatMessage($"@{chatter.Username} has joined the heist");
                    }
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdBankHeist(TwitchChatter)", false, "!bankheist");
            }
        }

        /// <summary>
        /// Display the broadcaster's subscriber link (if they're an Affiliate/Partner)
        /// </summary>
        /// <returns></returns>
        public async Task CmdSubscribe()
        {
            try
            {
                // Get broadcaster type and check if they can have subscribers
                ChannelJSON json = await _twitchInfo.GetBroadcasterChannelById();
                string broadcasterType = json.BroadcasterType;

                if (broadcasterType.Equals("partner") || broadcasterType.Equals("affiliate"))
                {
                    _irc.SendPublicChatMessage("Subscribe here! https://www.twitch.tv/subs/" + _botConfig.Broadcaster);
                }
                else
                {
                    _irc.SendPublicChatMessage($"{_botConfig.Broadcaster} is not a Twitch Affiliate/Partner. "
                        + "Please stick around and make their dream not a meme BlessRNG");
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdSubscribe()", false, "!sub");
            }
        }

        /// <summary>
        /// Engage in the boss fight minigame
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        public async Task CmdBossFight(TwitchChatter chatter)
        {
            try
            {
                BossFight bossFight = new BossFight();
                int funds = await _bank.CheckBalance(chatter.Username, _broadcasterId);

                if (_bossSettingsInstance.IsBossFightOnCooldown())
                {
                    TimeSpan cooldown = _bossSettingsInstance.CooldownTimePeriod.Subtract(DateTime.Now);

                    if (cooldown.Minutes >= 1)
                    {
                        _irc.SendPublicChatMessage(_bossSettingsInstance.CooldownEntry
                            .Replace("@timeleft@", cooldown.Minutes.ToString()));
                    }
                    else
                    {
                        _irc.SendPublicChatMessage(_bossSettingsInstance.CooldownEntry
                            .Replace("@timeleft@", cooldown.Seconds.ToString())
                            .Replace("minutes", "seconds"));
                    }

                    return;
                }

                if (_bossSettingsInstance.RefreshBossFight)
                {
                    _irc.SendPublicChatMessage($"The boss fight is currently being refreshed with new settings @{chatter.Username}");
                    return;
                }

                if (bossFight.HasFighterAlreadyEntered(chatter.Username))
                {
                    _irc.SendPublicChatMessage($"You are already in this fight @{chatter.Username}");
                    return;
                }

                if (funds < _bossSettingsInstance.Cost)
                {
                    _irc.SendPublicChatMessage($"You do need {_bossSettingsInstance.Cost} {_botConfig.CurrencyType} to enter this fight @{chatter.Username}");
                    return;
                }

                if (!bossFight.IsEntryPeriodOver())
                {
                    ChatterType chatterType = ChatterType.DoesNotExist;

                    // join boss fight
                    if (chatter.Badges.Contains("moderator")
                        || chatter.Badges.Contains("admin")
                        || chatter.Badges.Contains("global_mod")
                        || chatter.Badges.Contains("staff")
                        || chatter.Username == _botConfig.Broadcaster.ToLower())
                    {
                        chatterType = ChatterType.Moderator;
                    }
                    else if (chatter.Badges.Contains("subscriber") || chatter.Badges.Contains("vip"))
                    {
                        chatterType = ChatterType.Subscriber;
                    }
                    // ToDo: Create new columns in the BossFightClassStats table for VIP stats
                    //else if (chatter.Badges.Contains("vip"))
                    //{
                    //    chatterType = ChatterType.VIP;
                    //}
                    else
                    {
                        chatterType = _twitchChatterListInstance.GetUserChatterType(chatter.Username);
                        if (chatterType == ChatterType.DoesNotExist)
                        {
                            _irc.SendPublicChatMessage($"I'm not able to find you in the chatter list. Please try again in 15 seconds @{chatter.Username}");
                            return;
                        }
                    }

                    // make boss fight announcement if first fighter and start recruiting members
                    if (_bossSettingsInstance.Fighters.Count == 0)
                    {
                        _bossSettingsInstance.EntryPeriod = DateTime.Now.AddSeconds(_bossSettingsInstance.EntryPeriodSeconds);
                        _irc.SendPublicChatMessage(_bossSettingsInstance.EntryMessage.Replace("user@", chatter.Username));
                    }

                    FighterClass fighterClass = _bossSettingsInstance.ClassStats.Single(c => c.ChatterType == chatterType);
                    BossFighter fighter = new BossFighter { Username = chatter.Username, FighterClass = fighterClass };
                    bossFight.Produce(fighter);
                    await _bank.UpdateFunds(chatter.Username, _broadcasterId, funds - _bossSettingsInstance.Cost);

                    // display new boss level
                    if (!string.IsNullOrEmpty(bossFight.NextLevelMessage()))
                    {
                        _irc.SendPublicChatMessage(bossFight.NextLevelMessage());
                    }
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdBossFight(TwitchChatter)", false, "!raid");
            }
        }

        /// <summary>
        /// Tell the streamer the user is lurking
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        /// <returns></returns>
        public async Task<DateTime> CmdLurk(TwitchChatter chatter)
        {
            try
            {
                _irc.SendPublicChatMessage($"Okay {chatter.Username}! @{_botConfig.Broadcaster} will be waiting for you TPFufun");
                return DateTime.Now.AddMinutes(5);
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdLurk(TwitchChatter)", false, "!lurk");
            }

            return DateTime.Now;
        }

        /// <summary>
        /// Tell the streamer the user is back from lurking
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        /// <returns></returns>
        public async Task<DateTime> CmdUnlurk(TwitchChatter chatter)
        {
            try
            {
                _irc.SendPublicChatMessage($"Welcome back {chatter.Username}! KonCha I'll let @{_botConfig.Broadcaster} know you're here!");
                return DateTime.Now.AddMinutes(5);
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdUnlurk(TwitchChatter)", false, "!unlurk");
            }

            return DateTime.Now;
        }

        /// <summary>
        /// Let a user give an amount of their funds to another chatter
        /// </summary>
        /// <param name="chatter">User that sent the message</param>
        public async Task<DateTime> CmdGiveFunds(TwitchChatter chatter)
        {
            try
            {
                if (chatter.Message.StartsWith("!give @"))
                {
                    _irc.SendPublicChatMessage($"Please enter a valid amount @{chatter.Username} (ex: !give [amount/all] @[username])");
                    return DateTime.Now;
                }

                int giftAmount = 0;
                bool validGiftAmount = false;
                string giftMessage = chatter.Message.Substring(chatter.Message.IndexOf(" ") + 1, chatter.Message.GetNthCharIndex(' ', 2) - chatter.Message.IndexOf(" ") - 1);

                // Check if user wants to give all of their wallet to another user
                // Else check if their message is a valid amount to give
                validGiftAmount = giftMessage == "all" ? true : int.TryParse(giftMessage, out giftAmount);

                if (!validGiftAmount)
                {
                    _irc.SendPublicChatMessage($"Please insert a positive whole amount (no decimal numbers) to gamble @{chatter.Username}");
                    return DateTime.Now;
                }

                // Get and check recipient
                string recipient = chatter.Message.Substring(chatter.Message.IndexOf("@") + 1).ToLower();

                if (string.IsNullOrEmpty(recipient) || chatter.Message.IndexOf("@") == -1)
                {
                    _irc.SendPublicChatMessage($"I don't know who I'm supposed to send this to. Please specify a recipient @{chatter.Username}");
                    return DateTime.Now;
                }
                else if (recipient == chatter.Username)
                {
                    _irc.SendPublicChatMessage($"Stop trying to give {_botConfig.CurrencyType} to yourself @{chatter.Username}");
                    return DateTime.Now;
                }

                // Get and check wallet balance
                int balance = await _bank.CheckBalance(chatter.Username, _broadcasterId);

                if (giftMessage == "all")
                {
                    giftAmount = balance;
                }

                if (balance == -1)
                    _irc.SendPublicChatMessage($"You are not currently banking with us @{chatter.Username} . Please talk to a moderator about acquiring {_botConfig.CurrencyType}");
                else if (giftAmount < 1)
                    _irc.SendPublicChatMessage($"That is not a valid amount of {_botConfig.CurrencyType} to give. Please try again with a positive whole amount (no decimals) @{chatter.Username}");
                else if (balance < giftAmount)
                    _irc.SendPublicChatMessage($"You do not have enough to give {giftAmount} {_botConfig.CurrencyType} @{chatter.Username}");
                else
                {
                    // make sure the user exists in the database to prevent fake accounts from being created
                    int recipientBalance = await _bank.CheckBalance(recipient, _broadcasterId);

                    if (recipientBalance == -1)
                        _irc.SendPublicChatMessage($"The user \"{recipient}\" is currently not banking with us. Please talk to a moderator about creating their account @{chatter.Username}");
                    else
                    {
                        await _bank.UpdateFunds(chatter.Username, _broadcasterId, balance - giftAmount); // take away from sender
                        await _bank.UpdateFunds(recipient, _broadcasterId, giftAmount + recipientBalance); // give to recipient

                        _irc.SendPublicChatMessage($"@{chatter.Username} gave {giftAmount} {_botConfig.CurrencyType} to @{recipient}");
                        return DateTime.Now.AddSeconds(20);
                    }
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdGiveFunds(TwitchChatter)", false, "!give");
            }

            return DateTime.Now;
        }

        public async void CmdTwitterLink(bool hasTwitterInfo, string screenName)
        {
            try
            {
                if (!hasTwitterInfo)
                    _irc.SendPublicChatMessage($"Twitter username not found @{_botConfig.Broadcaster}");
                else if (string.IsNullOrEmpty(screenName))
                    _irc.SendPublicChatMessage("I'm sorry. I'm unable to get this broadcaster's Twitter handle/screen name");
                else
                    _irc.SendPublicChatMessage($"Check out this broadcaster's twitter at https://twitter.com/" + screenName);
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdTwitterLink(bool, string)", false, "!twitter");
            }
        }

        public async void CmdSupport()
        {
            try
            {
                _irc.SendPublicChatMessage("@Simple_Sandman is the source of all of my powers PowerUpL Jebaited PowerUpR "
                    + "Please check out his Twitch at https://twitch.tv/simple_sandman " 
                    + "If you need any support, send him a direct message at his Twitter https://twitter.com/Simple_Sandman "
                    + "Also, if you want to help me with power leveling, check out the Github https://github.com/SimpleSandman/TwitchBot");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdSupport()", false, "!support");
            }
        }

        public async Task CmdYouTubeCurrentSong(bool hasYouTubeAuth, TwitchChatter chatter)
        {
            try
            {
                string wpfTitle = "";

                Process[] processes = Process.GetProcessesByName("TwitchBotWpf");
                foreach (Process process in processes)
                {
                    wpfTitle = process.MainWindowTitle;
                    break;
                }

                if (wpfTitle.Contains("<<Playing>>"))
                {
                    CefSharpCache csCache = CefSharpCache.Load();

                    // Only get the title of the video
                    wpfTitle = wpfTitle.Replace("<<Playing>>", "");
                    wpfTitle = wpfTitle.Replace("==Bot DJ Mode ON==", "");
                    wpfTitle = wpfTitle.Replace("==Bot DJ Mode OFF==", "");

                    string playingMessage = $"Now Playing: \"{wpfTitle}\"";
                    string videoId = _youTubeClientInstance.GetYouTubeVideoId(csCache.Url);

                    if (!string.IsNullOrEmpty(videoId))
                    {
                        Video video = await _youTubeClientInstance.GetVideoById(videoId);

                        if (video.ContentDetails != null && video.Snippet != null)
                        {
                            string videoDuration = video.ContentDetails.Duration;
                            int timeIndex = videoDuration.IndexOf("T") + 1;
                            string parsedDuration = videoDuration.Substring(timeIndex);
                            int minIndex = parsedDuration.IndexOf("M");

                            string videoMin = "0";
                            string videoSec = "0";

                            if (minIndex > 0)
                                videoMin = parsedDuration.Substring(0, minIndex);

                            if (parsedDuration.IndexOf("S") > 0)
                                videoSec = parsedDuration.Substring(minIndex + 1).TrimEnd('S');

                            playingMessage = $"Now Playing: \"{video.Snippet.Title}\" by " +
                                $"{video.Snippet.ChannelTitle} ({videoMin}M{videoSec}S)";
                        }
                    }

                    _irc.SendPublicChatMessage(playingMessage);
                }
                else
                {
                    _irc.SendPublicChatMessage($"Nothing is playing at the moment @{chatter.Username}");
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdGen", "CmdWpfCurrentSong(bool, TwitchChatter)", false, "!song");
            }
        }

        private async Task<bool> IsMultiplayerGame(string username)
        {
            // Get current game name
            ChannelJSON json = await _twitchInfo.GetBroadcasterChannelById();
            string gameTitle = json.Game;

            // Grab game id in order to find party member
            TwitchGameCategory game = await _gameDirectory.GetGameId(gameTitle);

            if (string.IsNullOrEmpty(gameTitle))
            {
                _irc.SendPublicChatMessage("I cannot see the name of the game. It's currently set to either NULL or EMPTY. "
                    + "Please have the chat verify that the game has been set for this stream. "
                    + $"If the error persists, please have @{_botConfig.Broadcaster.ToLower()} retype the game in their Twitch Live Dashboard. "
                    + "If this error shows up again and your chat can see the game set for the stream, please contact my master with !support in this chat");
                return false;
            }
            else if (game == null || game.Id == 0)
            {
                _irc.SendPublicChatMessage($"I cannot find the game, \"{gameTitle}\", in the database. " 
                    + $"Have my master resolve this issue by typing !support in this chat @{username}");
                return false;
            }

            if (!game.Multiplayer)
            {
                _irc.SendPublicChatMessage("This game is set to single-player only. " 
                    + $"Contact my master with !support in this chat if this isn't correct @{username}");
                return false;
            }

            return true;
        }

        private bool ReactionCmd(string origUser, string recipient, string msgToSelf, string action, string addlMsg = "")
        {
            // check if user is trying to use a command on themselves
            if (origUser.Equals(recipient))
            {
                _irc.SendPublicChatMessage(msgToSelf + " @" + origUser);
                return true;
            }

            // check if recipient is the broadcaster before checking the viewer channel
            if (ChatterValid(origUser, recipient))
            {
                _irc.SendPublicChatMessage(origUser + " " + action + " @" + recipient + " " + addlMsg);
                return true;
            }

            return false;
        }

        private bool ChatterValid(string origUser, string recipient)
        {
            // Check if the requested user is this bot
            if (recipient.Equals(_botConfig.BotName.ToLower()) || recipient.Equals(_botConfig.Broadcaster.ToLower()))
                return true;

            // Wait until chatter lists are available
            while (!_twitchChatterListInstance.AreListsAvailable)
            {
                
            }

            // Grab user's chatter info (viewers, mods, etc.)
            List<string> chatterList = _twitchChatterListInstance.ChattersByName;
            if (chatterList.Count > 0)
            {
                // Search for user
                foreach (string chatter in chatterList)
                {
                    if (chatter.Equals(recipient.ToLower()))
                        return true;
                }
            }

            // finished searching with no results
            _irc.SendPublicChatMessage($"@{origUser}: I cannot find the user you wanted to interact with. Perhaps the user left us?");
            return false;
        }

        private string Effectiveness()
        {
            Random rnd = new Random(DateTime.Now.Millisecond);
            int effectiveLvl = rnd.Next(3); // between 0 and 2
            string effectiveness = "";

            if (effectiveLvl == 0)
                effectiveness = "It's super effective!";
            else if (effectiveLvl == 1)
                effectiveness = "It wasn't very effective";
            else
                effectiveness = "It had no effect";

            return effectiveness;
        }
    }
}
