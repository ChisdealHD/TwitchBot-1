﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using RestSharp;

using TwitchBot.Configuration;
using TwitchBot.Enums;
using TwitchBot.Libraries;
using TwitchBot.Services;
using TwitchBot.Models;
using TwitchBot.Models.JSON;

using TwitchBotDb.DTO;
using TwitchBotDb.Models;

using TwitchBotUtil.Extensions;

namespace TwitchBot.Commands
{
    public class CmdMod
    {
        private IrcClient _irc;
        private TimeoutCmd _timeout;
        private System.Configuration.Configuration _appConfig;
        private TwitchBotConfigurationSection _botConfig;
        private int _broadcasterId;
        private BankService _bank;
        private TwitchInfoService _twitchInfo;
        private ManualSongRequestService _manualSongRequest;
        private QuoteService _quote;
        private PartyUpService _partyUp;
        private GameDirectoryService _gameDirectory;
        private ErrorHandler _errHndlrInstance = ErrorHandler.Instance;
        private TwitterClient _twitter = TwitterClient.Instance;
        private BroadcasterSingleton _broadcasterInstance = BroadcasterSingleton.Instance;
        private TwitchChatterList _twitchChatterListInstance = TwitchChatterList.Instance;

        public CmdMod(IrcClient irc, TimeoutCmd timeout, TwitchBotConfigurationSection botConfig, int broadcasterId, 
            System.Configuration.Configuration appConfig, BankService bank, TwitchInfoService twitchInfo, ManualSongRequestService manualSongRequest,
            QuoteService quote, PartyUpService partyUp, GameDirectoryService gameDirectory)
        {
            _irc = irc;
            _timeout = timeout;
            _botConfig = botConfig;
            _broadcasterId = broadcasterId;
            _appConfig = appConfig;
            _bank = bank;
            _twitchInfo = twitchInfo;
            _manualSongRequest = manualSongRequest;
            _quote = quote;
            _partyUp = partyUp;
            _gameDirectory = gameDirectory;
        }

        /// <summary>
        /// Displays Discord link (if available)
        /// </summary>
        public async void CmdDiscord()
        {
            try
            {
                if (string.IsNullOrEmpty(_botConfig.DiscordLink) || _botConfig.DiscordLink.Equals("Link unavailable at the moment"))
                    _irc.SendPublicChatMessage("Discord link unavailable at the moment");
                else
                    _irc.SendPublicChatMessage("Wanna kick it with some awesome peeps like myself? Of course you do! Join this fantastic Discord! " + _botConfig.DiscordLink);
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdDiscord()", false, "!discord");
            }
        }

        /// <summary>
        /// Takes money away from a user
        /// </summary>
        /// <param name="chatter"></param>
        public async Task CmdCharge(TwitchChatter chatter)
        {
            try
            {
                if (chatter.Message.StartsWith("!charge @", StringComparison.CurrentCultureIgnoreCase))
                    _irc.SendPublicChatMessage($"Please enter a valid amount @{chatter.Username}");
                else
                {
                    int indexAction = chatter.Message.IndexOf(" ");
                    int fee = -1;
                    bool isValidFee = int.TryParse(chatter.Message.Substring(indexAction, chatter.Message.IndexOf("@") - indexAction - 1), out fee);
                    string recipient = chatter.Message.Substring(chatter.Message.IndexOf("@") + 1).ToLower();
                    int wallet = await _bank.CheckBalance(recipient, _broadcasterId);

                    // Check user's bank account exist or has currency
                    if (wallet == -1)
                        _irc.SendPublicChatMessage($"{recipient} is not currently banking with us @{chatter.Username}");
                    else if (wallet == 0)
                        _irc.SendPublicChatMessage($"{recipient} is out of {_botConfig.CurrencyType} @{chatter.Username}");
                    // Check if fee can be accepted
                    else if (fee > 0)
                        _irc.SendPublicChatMessage("Please insert a negative whole amount (no decimal numbers) "
                            + $" or use the !deposit command to add {_botConfig.CurrencyType} to a user's account");
                    else if (!isValidFee)
                        _irc.SendPublicChatMessage($"The fee wasn't accepted. Please try again with negative whole amount (no decimals) @{chatter.Username}");
                    else if (chatter.Username != _botConfig.Broadcaster.ToLower() && _twitchChatterListInstance.GetUserChatterType(recipient) == ChatterType.Moderator)
                        _irc.SendPublicChatMessage($"Entire deposit voided. You cannot remove {_botConfig.CurrencyType} from another moderator's account @{chatter.Username}");
                    else /* Deduct funds from wallet */
                    {
                        wallet += fee;

                        // Zero out account balance if user is being charged more than they have
                        if (wallet < 0)
                            wallet = 0;

                        await _bank.UpdateFunds(recipient, _broadcasterId, wallet);

                        // Prompt user's balance
                        if (wallet == 0)
                            _irc.SendPublicChatMessage($"Charged {fee.ToString().Replace("-", "")} {_botConfig.CurrencyType} to {recipient}"
                                + $"'s account! They are out of {_botConfig.CurrencyType} to spend");
                        else
                            _irc.SendPublicChatMessage($"Charged {fee.ToString().Replace("-", "")} {_botConfig.CurrencyType} to {recipient}"
                                + $"'s account! They only have {wallet} {_botConfig.CurrencyType} to spend");
                    }
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdCharge(TwitchChatter)", false, "!charge");
            }
        }

        /// <summary>
        /// Gives a set amount of stream currency to user
        /// </summary>
        /// <param name="chatter"></param>
        public async Task CmdDeposit(TwitchChatter chatter)
        {
            try
            {
                List<string> userList = new List<string>();

                foreach (int index in chatter.Message.AllIndexesOf("@"))
                {
                    int lengthUsername = chatter.Message.IndexOf(" ", index) - index - 1;
                    if (lengthUsername < 0)
                        userList.Add(chatter.Message.Substring(index + 1).ToLower());
                    else
                        userList.Add(chatter.Message.Substring(index + 1, lengthUsername).ToLower());
                }

                // Check for valid command
                if (chatter.Message.StartsWith("!deposit @", StringComparison.CurrentCultureIgnoreCase))
                    _irc.SendPublicChatMessage($"Please enter a valid amount to a user @{chatter.Username}");
                // Check if moderator is trying to give streamer currency to themselves
                else if (chatter.Username != _botConfig.Broadcaster.ToLower() && userList.Contains(chatter.Username))
                    _irc.SendPublicChatMessage($"Entire deposit voided. You cannot add {_botConfig.CurrencyType} to your own account @{chatter.Username}");
                else
                {
                    // Check if moderator is trying to give streamer currency to other moderators
                    if (chatter.Username != _botConfig.Broadcaster.ToLower())
                    {
                        foreach (string user in userList)
                        {
                            if (_twitchChatterListInstance.GetUserChatterType(user) == ChatterType.Moderator)
                            {
                                _irc.SendPublicChatMessage($"Entire deposit voided. You cannot add {_botConfig.CurrencyType} to another moderator's account @{chatter.Username}");
                                return;
                            }
                        }
                    }

                    int indexAction = chatter.Message.IndexOf(" ");
                    int deposit = -1;
                    bool isValidDeposit = int.TryParse(chatter.Message.Substring(indexAction, chatter.Message.IndexOf("@") - indexAction - 1), out deposit);

                    // Check if deposit amount is valid
                    if (deposit < 0)
                        _irc.SendPublicChatMessage("Please insert a positive whole amount (no decimals) " 
                            + $" or use the !charge command to remove {_botConfig.CurrencyType} from a user");
                    else if (!isValidDeposit)
                        _irc.SendPublicChatMessage($"The deposit wasn't accepted. Please try again with a positive whole amount (no decimals) @{chatter.Username}");
                    else
                    {
                        if (userList.Count > 0)
                        {
                            List<BalanceResult> balResultList = await _bank.UpdateCreateBalance(userList, _broadcasterId, deposit, true);

                            string responseMsg = $"Gave {deposit.ToString()} {_botConfig.CurrencyType} to ";

                            if (balResultList.Count > 1)
                            {
                                foreach (BalanceResult userResult in balResultList)
                                    responseMsg += $"{userResult.Username}, ";

                                responseMsg = responseMsg.ReplaceLastOccurrence(", ", "");
                            }
                            else if (balResultList.Count == 1)
                            {
                                responseMsg += $"@{balResultList[0].Username} ";

                                if (balResultList[0].ActionType.Equals("UPDATE"))
                                    responseMsg += $"and now has {balResultList[0].Wallet} {_botConfig.CurrencyType}!";
                                else if (balResultList[0].ActionType.Equals("INSERT"))
                                    responseMsg += $"and can now gamble it all away! Kappa";
                            }
                            else
                                responseMsg = $"Unknown error has occurred in retrieving results. Please check your recipient's {_botConfig.CurrencyType}";

                            _irc.SendPublicChatMessage(responseMsg);
                        }
                        else
                        {
                            _irc.SendPublicChatMessage($"There are no chatters to deposit {_botConfig.CurrencyType} @{chatter.Username}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdDeposit(TwitchChatter)", false, "!deposit");
            }
        }

        /// <summary>
        /// Gives every viewer currently watching a set amount of currency
        /// </summary>
        /// <param name="chatter"></param>
        public async Task CmdBonusAll(TwitchChatter chatter)
        {
            try
            {
                // Check for valid command
                if (chatter.Message.StartsWith("!bonusall @", StringComparison.CurrentCultureIgnoreCase))
                    _irc.SendPublicChatMessage($"Please enter a valid amount to a user @{chatter.Username}");
                else
                {
                    int indexAction = chatter.Message.IndexOf(" ");
                    int deposit = -1;
                    bool isValidDeposit = int.TryParse(chatter.Message.Substring(indexAction), out deposit);

                    // Check if deposit amount is valid
                    if (deposit < 0)
                        _irc.SendPublicChatMessage("Please insert a positive whole amount (no decimals) "
                            + $" or use the !charge command to remove {_botConfig.CurrencyType} from a user");
                    else if (!isValidDeposit)
                        _irc.SendPublicChatMessage($"The bulk deposit wasn't accepted. Please try again with positive whole amount (no decimals) @{chatter.Username}");
                    else
                    {
                        // Wait until chatter lists are available
                        while (!_twitchChatterListInstance.AreListsAvailable)
                        {

                        }

                        List<string> chatterList = _twitchChatterListInstance.ChattersByName;

                        // broadcaster gives stream currency to all but themselves and the bot
                        if (chatter.Username == _botConfig.Broadcaster.ToLower())
                        {
                            chatterList = chatterList.Where(t => t != chatter.Username.ToLower() && t != _botConfig.BotName.ToLower()).ToList();
                        }
                        else // moderators gives stream currency to all but other moderators (including broadcaster)
                        {
                            chatterList = chatterList
                                .Where(t => _twitchChatterListInstance.GetUserChatterType(t) != ChatterType.Moderator 
                                    && t != _botConfig.BotName.ToLower()).ToList();
                        }

                        if (chatterList != null && chatterList.Count > 0)
                        {
                            await _bank.UpdateCreateBalance(chatterList, _broadcasterId, deposit);

                            _irc.SendPublicChatMessage($"{deposit.ToString()} {_botConfig.CurrencyType} for everyone! "
                                + $"Check your stream bank account with !{_botConfig.CurrencyType.ToLower()}");
                        }
                        else
                        {
                            _irc.SendPublicChatMessage($"There are no chatters to deposit {_botConfig.CurrencyType} @{chatter.Username}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdBonusAll(string, string)", false, "!bonusall");
            }
        }

        /// <summary>
        /// Removes the first song in the queue of song requests
        /// </summary>
        public async Task CmdPopManualSr()
        {
            try
            {
                SongRequest removedSong = await _manualSongRequest.PopSongRequest(_broadcasterId);

                if (removedSong != null)
                    _irc.SendPublicChatMessage($"The first song in the queue, \"{removedSong.Requests}\" ({removedSong.Chatter}), has been removed");
                else
                    _irc.SendPublicChatMessage("There are no songs that can be removed from the song request list");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdPopManualSr()", false, "!poprbsr");
            }
        }

        /// <summary>
        /// Resets the song request queue
        /// </summary>
        public async Task CmdResetManualSr()
        {
            try
            {
                List<SongRequest> removedSong = await _manualSongRequest.ResetSongRequests(_broadcasterId);

                if (removedSong != null && removedSong.Count > 0)
                    _irc.SendPublicChatMessage($"The song request queue has been reset @{_botConfig.Broadcaster}");
                else
                    _irc.SendPublicChatMessage($"Song requests are empty @{_botConfig.Broadcaster}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdResetManualSr()", false, "!resetrbsr");
            }
        }

        /// <summary>
        /// Removes first party memeber in queue of party up requests
        /// </summary>
        public async Task CmdPopPartyUpRequest()
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
                else if (game?.Id > 0)
                    _irc.SendPublicChatMessage(await _partyUp.PopRequestedPartyMember(game.Id, _broadcasterId));
                else
                    _irc.SendPublicChatMessage("This game is not part of the \"Party Up\" system");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdPopPartyUpRequest()", false, "!poppartyuprequest");
            }
        }

        /// <summary>
        /// Bot-specific timeout on a user for a set amount of time
        /// </summary>
        /// <param name="chatter"></param>
        public async Task CmdAddTimeout(TwitchChatter chatter)
        {
            try
            {
                if (chatter.Message.StartsWith("!addtimeout @"))
                    _irc.SendPublicChatMessage("I cannot make a user not talk to me without this format '!addtimeout [seconds] @[username]'");
                else if (chatter.Message.ToLower().Contains(_botConfig.Broadcaster.ToLower()))
                    _irc.SendPublicChatMessage($"I cannot betray @{_botConfig.Broadcaster} by not allowing him to communicate with me @{chatter.Username}");
                else if (chatter.Message.ToLower().Contains(_botConfig.BotName.ToLower()))
                    _irc.SendPublicChatMessage($"You can't time me out @{chatter.Username} PowerUpL Jebaited PowerUpR");
                else
                {
                    int indexAction = chatter.Message.IndexOf(" ");
                    string recipient = chatter.Message.Substring(chatter.Message.IndexOf("@") + 1).ToLower();
                    double seconds = -1;
                    bool isValidTimeout = double.TryParse(chatter.Message.Substring(indexAction, chatter.Message.IndexOf("@") - indexAction - 1), out seconds);

                    if (!isValidTimeout || seconds < 0.00)
                        _irc.SendPublicChatMessage("The timeout amount wasn't accepted. Please try again with positive seconds only");
                    else if (seconds < 15.00)
                        _irc.SendPublicChatMessage("The duration needs to be at least 15 seconds long. Please try again");
                    else
                    {
                        DateTime timeoutExpiration = await _timeout.AddTimeout(recipient, _broadcasterId, seconds, _botConfig.TwitchBotApiLink);

                        string response = $"I'm told not to talk to you until {timeoutExpiration.ToLocalTime()} ";

                        if (timeoutExpiration.ToLocalTime().IsDaylightSavingTime())
                            response += $"({TimeZone.CurrentTimeZone.DaylightName})";
                        else
                            response += $"({TimeZone.CurrentTimeZone.StandardName})";

                        _irc.SendPublicChatMessage($"{response} @{recipient}");
                    }
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdAddTimeout(TwitchChatter)", false, "!addtimeout");
            }
        }

        /// <summary>
        /// Remove bot-specific timeout on a user for a set amount of time
        /// </summary>
        /// <param name="chatter"></param>
        public async Task CmdDeleteTimeout(TwitchChatter chatter)
        {
            try
            {
                string recipient = chatter.Message.Substring(chatter.Message.IndexOf("@") + 1).ToLower();

                recipient = await _timeout.DeleteUserTimeout(recipient, _broadcasterId, _botConfig.TwitchBotApiLink);

                if (!string.IsNullOrEmpty(recipient))
                    _irc.SendPublicChatMessage($"{recipient} can now interact with me again because of @{chatter.Username} @{_botConfig.Broadcaster}");
                else
                    _irc.SendPublicChatMessage($"Cannot find the user you wish to timeout @{chatter.Username}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdDelTimeout(TwitchChatter)", false, "!deltimeout");
            }
        }

        /// <summary>
        /// Set delay for messages based on the latency of the stream
        /// </summary>
        /// <param name="chatter"></param>
        public async void CmdSetLatency(TwitchChatter chatter)
        {
            try
            {
                int latency = -1;
                bool isValidInput = int.TryParse(chatter.Message.Substring(chatter.Message.IndexOf(" ")), out latency);

                if (!isValidInput || latency < 0)
                    _irc.SendPublicChatMessage("Please insert a valid positive alloted amount of time (in seconds)");
                else
                {
                    _botConfig.StreamLatency = latency;
                    _appConfig.AppSettings.Settings.Remove("streamLatency");
                    _appConfig.AppSettings.Settings.Add("streamLatency", latency.ToString());
                    _appConfig.Save(ConfigurationSaveMode.Modified);
                    ConfigurationManager.RefreshSection("TwitchBotConfiguration");

                    _irc.SendPublicChatMessage($"Bot settings for stream latency set to {_botConfig.StreamLatency} second(s) @{chatter.Username}");
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdSetLatency(TwitchChatter)", false, "!setlatency");
            }
        }

        /// <summary>
        /// Add a mod/broadcaster quote
        /// </summary>
        /// <param name="chatter"></param>
        public async Task CmdAddQuote(TwitchChatter chatter)
        {
            try
            {
                string quote = chatter.Message.Substring(chatter.Message.IndexOf(" ") + 1);

                await _quote.AddQuote(quote, chatter.Username, _broadcasterId);

                _irc.SendPublicChatMessage($"Quote has been created @{chatter.Username}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdAddQuote(string, string)", false, "!addquote");
            }
        }

        /// <summary>
        /// Tell the stream the specified moderator will be AFK
        /// </summary>
        /// <param name="chatter"></param>
        public async void CmdModAfk(TwitchChatter chatter)
        {
            try
            {
                _irc.SendPublicChatMessage($"@{chatter.Username} is going AFK @{_botConfig.Broadcaster}! SwiftRage");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdModAfk(string)", false, "!modafk");
            }
        }

        /// <summary>
        /// Tell the stream the specified moderator is back
        /// </summary>
        /// <param name="chatter"></param>
        public async void CmdModBack(TwitchChatter chatter)
        {
            try
            {
                _irc.SendPublicChatMessage($"@{chatter.Username} is back @{_botConfig.Broadcaster}! BlessRNG");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdModBack(string)", false, "!modback");
            }
        }

        /// <summary>
        /// Add user(s) to a MultiStream link so viewers can watch multiple streamers at the same time
        /// </summary>
        /// <param name="chatter"></param>
        /// <param name="multiStreamUsers">List of users that have already been added to the link</param>
        public async Task<List<string>> CmdAddMultiStreamUser(TwitchChatter chatter, List<string> multiStreamUsers)
        {
            try
            {
                int userLimit = 3;

                // Hard-coded limit to 4 users (including broadcaster) 
                // because of possible video bandwidth issues for users...for now
                if (multiStreamUsers.Count >= userLimit)
                    _irc.SendPublicChatMessage($"Max limit of users set for the MultiStream link! Please reset the link @{chatter.Username}");
                else if (chatter.Message.IndexOf("@") == -1)
                    _irc.SendPublicChatMessage($"Please use the \"@\" to define new user(s) to add @{chatter.Username}");
                else if (chatter.Message.Contains(_botConfig.Broadcaster, StringComparison.CurrentCultureIgnoreCase)
                            || chatter.Message.Contains(_botConfig.BotName, StringComparison.CurrentCultureIgnoreCase))
                {
                    _irc.SendPublicChatMessage($"I cannot add the broadcaster or myself to the MultiStream link @{chatter.Username}");
                }
                else
                {
                    List<int> indexNewUsers = chatter.Message.AllIndexesOf("@");

                    if (multiStreamUsers.Count + indexNewUsers.Count > userLimit)
                        _irc.SendPublicChatMessage("Too many users are being added to the MultiStream link " + 
                            $"< Number of users already added: \"{multiStreamUsers.Count}\" >" + 
                            $"< User limit (without broadcaster): \"{userLimit}\" > @{chatter.Username}");
                    else
                    {
                        string setMultiStreamUsers = "";
                        string verbUsage = "has ";

                        if (indexNewUsers.Count == 1)
                        {
                            string newUser = chatter.Message.Substring(indexNewUsers[0] + 1);

                            if (!multiStreamUsers.Contains(newUser.ToLower()))
                            {
                                multiStreamUsers.Add(newUser.ToLower());
                                setMultiStreamUsers = $"@{newUser.ToLower()} ";
                            }
                            else
                            {
                                setMultiStreamUsers = $"{newUser} ";
                                verbUsage = "has already ";
                            }
                        }
                        else
                        {
                            for (int i = 0; i < indexNewUsers.Count; i++)
                            {
                                int indexNewUser = indexNewUsers[i] + 1;
                                string setMultiStreamUser = "";

                                if (i + 1 < indexNewUsers.Count)
                                    setMultiStreamUser = chatter.Message.Substring(indexNewUser, indexNewUsers[i + 1] - indexNewUser - 1).ToLower();
                                else
                                    setMultiStreamUser = chatter.Message.Substring(indexNewUser).ToLower();

                                if (!multiStreamUsers.Contains(setMultiStreamUser))
                                    multiStreamUsers.Add(setMultiStreamUser.ToLower());
                            }

                            foreach (string multiStreamUser in multiStreamUsers)
                                setMultiStreamUsers += $"@{multiStreamUser} ";

                            verbUsage = "have ";
                        }

                        string resultMsg = $"{setMultiStreamUsers} {verbUsage} been set up for the MultiStream link @{chatter.Username}";

                        if (chatter.Username.ToLower().Equals(_botConfig.Broadcaster.ToLower()))
                            _irc.SendPublicChatMessage(resultMsg);
                        else
                            _irc.SendPublicChatMessage($"{resultMsg} @{_botConfig.Broadcaster.ToLower()}");
                    }
                }

                return multiStreamUsers;
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdAddMultiStreamUser(string, string, ref List<string>)", false, "!addmsl", chatter.Message);
            }

            return new List<string>();
        }

        /// <summary>
        /// Reset the MultiStream link to allow the link to be reconfigured
        /// </summary>
        /// <param name="chatter"></param>
        /// <param name="multiStreamUsers">List of users that have already been added to the link</param>
        public async Task<List<string>> CmdResetMultiStreamLink(TwitchChatter chatter, List<string> multiStreamUsers)
        {
            try
            {
                multiStreamUsers = new List<string>();

                string resultMsg = "MultiStream link has been reset. " + 
                    $"Please reconfigure the link if you are planning on using it in the near future @{chatter.Username}";

                if (chatter.Username == _botConfig.Broadcaster.ToLower())
                    _irc.SendPublicChatMessage(resultMsg);
                else
                    _irc.SendPublicChatMessage($"{resultMsg} @{_botConfig.Broadcaster.ToLower()}");

                return multiStreamUsers;
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdResetMultiStream(string, ref List<string>)", false, "!resetmsl");
            }

            return new List<string>();
        }

        /// <summary>
        /// Update the title of the Twitch channel
        /// </summary>
        /// <param name="chatter"></param>
        public async Task CmdUpdateTitle(TwitchChatter chatter)
        {
            try
            {
                // Get title from command parameter
                string title = chatter.Message.Substring(chatter.Message.IndexOf(" ") + 1);

                // Send HTTP method PUT to base URI in order to change the title
                RestClient client = new RestClient("https://api.twitch.tv/kraken/channels/" + _broadcasterInstance.TwitchId);
                RestRequest request = new RestRequest(Method.PUT);
                request.AddHeader("Cache-Control", "no-cache");
                request.AddHeader("Content-Type", "application/json");
                request.AddHeader("Authorization", "OAuth " + _botConfig.TwitchAccessToken);
                request.AddHeader("Accept", "application/vnd.twitchtv.v5+json");
                request.AddHeader("Client-ID", _botConfig.TwitchClientId);
                request.AddParameter("application/json", "{\"channel\":{\"status\":\"" + title + "\"}}",
                    ParameterType.RequestBody);

                IRestResponse response = null;
                try
                {
                    response = await client.ExecuteTaskAsync<Task>(request);
                    string statResponse = response.StatusCode.ToString();
                    if (statResponse.Contains("OK"))
                    {
                        _irc.SendPublicChatMessage($"Twitch channel title updated to \"{title}\"");
                    }
                    else
                        Console.WriteLine(response.ErrorMessage);
                }
                catch (WebException ex)
                {
                    if (((HttpWebResponse)ex.Response).StatusCode == HttpStatusCode.BadRequest)
                    {
                        Console.WriteLine("Error 400 detected!");
                    }
                    response = (IRestResponse)ex.Response;
                    Console.WriteLine("Error: " + response);
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdUpdateTitle(TwitchChatter)", false, "!updatetitle");
            }
        }

        /// <summary>
        /// Updates the game being played on the Twitch channel
        /// </summary>
        /// <param name="chatter"></param>
        /// <param name="hasTwitterInfo">Check for Twitter credentials</param>
        public async Task CmdUpdateGame(TwitchChatter chatter, bool hasTwitterInfo)
        {
            try
            {
                // Get game from command parameter
                string game = chatter.Message.Substring(chatter.Message.IndexOf(" ") + 1);

                // Send HTTP method PUT to base URI in order to change the game
                RestClient client = new RestClient("https://api.twitch.tv/kraken/channels/" + _broadcasterInstance.TwitchId);
                RestRequest request = new RestRequest(Method.PUT);
                request.AddHeader("Cache-Control", "no-cache");
                request.AddHeader("Content-Type", "application/json");
                request.AddHeader("Authorization", "OAuth " + _botConfig.TwitchAccessToken);
                request.AddHeader("Accept", "application/vnd.twitchtv.v5+json");
                request.AddHeader("Client-ID", _botConfig.TwitchClientId);
                request.AddParameter("application/json", "{\"channel\":{\"game\":\"" + game + "\"}}",
                    ParameterType.RequestBody);

                IRestResponse response = null;
                try
                {
                    response = await client.ExecuteTaskAsync<Task>(request);
                    string statResponse = response.StatusCode.ToString();
                    if (statResponse.Contains("OK"))
                    {
                        _irc.SendPublicChatMessage($"Twitch channel game status updated to \"{game}\"");
                        if (_botConfig.EnableTweets && hasTwitterInfo)
                        {
                            Console.WriteLine(_twitter.SendTweet($"Just switched to \"{game}\" on " 
                                + $"twitch.tv/{_broadcasterInstance.Username}"));
                        }

                        await Threads.ChatReminder.RefreshReminders();
                    }
                    else
                    {
                        Console.WriteLine(response.Content);
                    }
                }
                catch (WebException ex)
                {
                    if (((HttpWebResponse)ex.Response).StatusCode == HttpStatusCode.BadRequest)
                    {
                        Console.WriteLine("Error 400 detected!!");
                    }
                    response = (IRestResponse)ex.Response;
                    Console.WriteLine("Error: " + response);
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdUpdateGame(TwitchChatter, bool)", false, "!updategame");
            }
        }

        public async Task<Queue<string>> CmdPopJoin(TwitchChatter chatter, Queue<string> gameQueueUsers)
        {
            try
            {
                if (gameQueueUsers.Count == 0)
                    _irc.SendPublicChatMessage($"Queue is empty @{chatter.Username}");
                else
                {
                    string poppedUser = gameQueueUsers.Dequeue();
                    _irc.SendPublicChatMessage($"{poppedUser} has been removed from the queue @{chatter.Username}");
                }
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdPopJoin(TwitchChatter, Queue<string>)", false, "!popjoin");
            }

            return gameQueueUsers;
        }

        public async Task<Queue<string>> CmdResetJoin(TwitchChatter chatter, Queue<string> gameQueueUsers)
        {
            try
            {
                if (gameQueueUsers.Count != 0)
                    gameQueueUsers.Clear();

                _irc.SendPublicChatMessage($"Queue is empty @{chatter.Username}");
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdResetJoin(TwitchChatter, Queue<string>)", false, "!resetjoin");
            }

            return gameQueueUsers;
        }

        public async Task CmdPromoteStreamer(TwitchChatter chatter)
        {
            try
            {
                string streamerUsername = chatter.Message.Substring(chatter.Message.IndexOf("@") + 1).ToLower();

                RootUserJSON userInfo = await _twitchInfo.GetUsersByLoginName(streamerUsername);
                if (userInfo.Users.Count == 0)
                {
                    _irc.SendPublicChatMessage($"Cannot find the requested user @{chatter.Username}");
                    return;
                }

                string userId = userInfo.Users.First().Id;
                string promotionMessage = $"Hey everyone! Check out {streamerUsername}'s channel at https://www.twitch.tv/" 
                    + $"{streamerUsername} and slam that follow button!";

                RootStreamJSON userStreamInfo = await _twitchInfo.GetUserStream(userId);

                if (userStreamInfo.Stream == null)
                {
                    ChannelJSON channelInfo = await _twitchInfo.GetUserChannelById(userId);

                    if (!string.IsNullOrEmpty(channelInfo.Game))
                        promotionMessage += $" They were last seen playing \"{channelInfo.Game}\"";
                }
                else
                {
                    if (!string.IsNullOrEmpty(userStreamInfo.Stream.Game))
                        promotionMessage += $" Right now, they're playing \"{userStreamInfo.Stream.Game}\"";
                }

                _irc.SendPublicChatMessage(promotionMessage);
            }
            catch (Exception ex)
            {
                await _errHndlrInstance.LogError(ex, "CmdMod", "CmdResetGotNextGame(TwitchChatter)", false, "!streamer");
            }
        }
    }
}
