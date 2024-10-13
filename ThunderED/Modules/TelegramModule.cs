using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Matrix.Xmpp.XHtmlIM;

using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using ThunderED.Classes;
using ThunderED.Helpers;

namespace ThunderED.Modules
{
    public class TelegramModule: AppModuleBase, IDiscordRelayModule
    {
        public override LogCat Category => LogCat.Telegram;
        private User _me;
        private TelegramBotClient _client;
        private readonly List<string> _messagePool = new List<string>();
        public event Action<string, ulong> RelayMessage;

        public override void Cleanup()
        {
            _client?.StopReceiving();
            _client = null;
            _me = null;
        }

        public override async Task Initialize()
        {
            await LogHelper.LogModule("Initializing Telegram module...", Category);
        }

        public override async Task Run(object prm)
        {
            if (IsRunning || _me != null) return;
            IsRunning = true;
            try
            {
                var TelegramChannelData = Settings.TelegramModule.GetEnabledGroups().ToDictionary(pair => pair.Key, pair => pair.Value);
                await LogHelper.LogModule("Initializing Telegram module...", Category);
                if (Settings.TelegramModule == null || string.IsNullOrEmpty(Settings.TelegramModule.Token))
                {
                    await LogHelper.LogError("Token is not set for Telegram module!", Category);
                    return;
                }
                foreach (var (channelname, channel) in TelegramChannelData) {
                    if (channel.Telegram == 0)
                    {
                        await SendOneTimeWarning(channelname, $" No relay channels set for Telegram module!");
                        return;
                    }
                }

                HttpClient cli = null;
                IWebProxy proxy = null;
                if (!string.IsNullOrEmpty(Settings.TelegramModule.ProxyAddress) && Settings.TelegramModule.ProxyPort != 0)
                {
                    var url = $"{Settings.TelegramModule.ProxyAddress}:{Settings.TelegramModule.ProxyPort}";
                    ICredentials cr = null;
                    if (!string.IsNullOrEmpty(Settings.TelegramModule.ProxyUsername))
                    {
                        cr = new NetworkCredential(Settings.TelegramModule.ProxyUsername, Settings.TelegramModule.ProxyPassword);
                    }
                    proxy = new WebProxy(new Uri(url), true, null, cr);
                    cli = new HttpClient(new SocketsHttpHandler {Proxy = proxy});
                }

                _client = new TelegramBotClient(Settings.TelegramModule.Token, cli);
                _client.OnMessage += BotClient_OnMessage;
                if (!await _client.TestApiAsync())
                {
                    await LogHelper.LogError("API ERROR!", Category);
                    return;
                }
                _client.OnReceiveError += _client_OnReceiveError;
                _client.OnReceiveGeneralError += _client_OnReceiveGeneralError;
                _me = await _client.GetMeAsync();
                _client.StartReceiving();
                await LogHelper.LogInfo("Telegram bot connected!", Category);

            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(ex.Message, ex, Category);
            }
            finally
            {
                IsRunning = false;
            }
        }

        private void _client_OnReceiveGeneralError(object sender, Telegram.Bot.Args.ReceiveGeneralErrorEventArgs e)
        {
            LogHelper.LogEx($"General Error: {e.Exception.Message}", e.Exception, Category).ConfigureAwait(false);
        }

        private void _client_OnReceiveError(object sender, Telegram.Bot.Args.ReceiveErrorEventArgs e)
        {
            LogHelper.LogEx($"API Error: {e.ApiRequestException.Message}", e.ApiRequestException, Category).ConfigureAwait(false);

        }

        private void BotClient_OnMessage(object sender, Telegram.Bot.Args.MessageEventArgs e)
        {
            LogHelper.LogDebug($"Received telegram message from {e.Message.Chat.Id}, processing", Category);

            if(e.Message.Type != MessageType.Text || e.Message.Chat.Type == ChatType.Private || !APIHelper.IsDiscordAvailable) return;

            if (!Settings.TelegramModule.RelayFromTelegram) return;
            
            var TelegramChannelData = Settings.TelegramModule.GetEnabledGroups().ToDictionary(pair => pair.Key, pair => pair.Value);

            foreach (var (channelname, channel) in TelegramChannelData)
            {
                //var relay = Settings.TelegramModule.RelayChannels.FirstOrDefault(a=> a.Telegram == e.Message.Chat.Id);
                LogHelper.LogDebug($"Decision tree - telegram settings channel {channelname}", Category);

                var relay = channel;
                if (relay == null || relay.Telegram != e.Message.Chat.Id) return;
                if(relay.Discord == 0 || IsMessagePooled(e.Message.Text) || relay.TelegramFilters.Any(e.Message.Text.Contains) || relay.TelegramFiltersStartsWith.Any(e.Message.Text.StartsWith)) return;

                var fromNick = $"{e.Message.From.FirstName} {e.Message.From.LastName}";
                var fromName = e.Message.From.Username;
                if(relay.TelegramUsers.Count > 0 && !relay.TelegramUsers.Contains(fromName) && !relay.TelegramUsers.Contains(fromNick)) return;

                var name = string.IsNullOrWhiteSpace(fromNick) ? fromName : fromNick;
                var msg = $"[TG][{name}]: {e.Message.Text}";
                UpdatePool(msg);
                RelayMessage?.Invoke(msg, relay.Discord);
            }
        }

        public async Task SendMessage(ulong channel, ulong authorId, string user, string message)
        {
            LogHelper.LogDebug($"Sendmessage block for Discord {channel}", Category);

            if(_me == null || !APIHelper.IsDiscordAvailable) return;
            if(!Settings.TelegramModule.RelayFromDiscord) return;

            var TelegramChannelData = Settings.TelegramModule.GetEnabledGroups().ToDictionary(pair => pair.Key, pair => pair.Value);

            foreach (var (channelname, chan) in TelegramChannelData)
            {
                LogHelper.LogDebug($"Decision tree for {channelname}", Category);

                var relay = chan;
                if(relay == null || relay.Discord != channel) return;
                //filter by denial
                if(relay.Telegram == 0 || IsMessagePooled(message) || relay.DiscordFilters.Any(message.Contains) || relay.DiscordFiltersStartsWith.Any(message.StartsWith)) return;
                //filter by allowance
                if(relay.DiscordAllowFilters.Any() && !relay.DiscordAllowFilters.Any(a=> message.Contains(a, StringComparison.OrdinalIgnoreCase))) return;
                //check if we relay only bot messages
                if (relay.RelayFromDiscordBotOnly)
                {
                    var u = APIHelper.DiscordAPI.GetUser(authorId);
                    if(u==null || APIHelper.DiscordAPI.GetCurrentUser().Id != u.Id) return;
                }

                var msg = $"[DS][{user}]: {message}";
                UpdatePool(msg);
                await _client.SendTextMessageAsync(relay.Telegram, msg);
            }
        }

        #region Pooling
        private bool IsMessagePooled(string message)
        {
            return !string.IsNullOrEmpty(_messagePool.FirstOrDefault(a => a == message));
        }

        private void UpdatePool(string message)
        {
            _messagePool.Add(message);
            if(_messagePool.Count > 10)
                _messagePool.RemoveAt(0);
        }
        #endregion
    }
}
