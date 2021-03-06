﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Discord;
using ThunderED.Classes;
using ThunderED.Helpers;
using ThunderED.Json;
using ThunderED.Modules.OnDemand;
using ThunderED.Modules.Sub;

namespace ThunderED.Modules
{
    public class MailModule: AppModuleBase
    {
        public override LogCat Category => LogCat.Mail;

        private readonly int _checkInterval;
        private DateTime _lastCheckTime = DateTime.MinValue;

        public MailModule()
        {
            _checkInterval = Settings.MailModule.CheckIntervalInMinutes;
            if (_checkInterval == 0)
                _checkInterval = 1;
            WebServerModule.ModuleConnectors.Add(Reason, OnAuthRequest);
        }

        private async Task<bool> OnAuthRequest(HttpListenerRequestEventArgs context)
        {
            if (!Settings.Config.ModuleMail) return false;

            var request = context.Request;
            var response = context.Response;

            try
            {
                var extPort = Settings.WebServerModule.WebExternalPort;
                var port = Settings.WebServerModule.WebListenPort;

                if (request.HttpMethod == HttpMethod.Get.ToString())
                {
                    if (request.Url.LocalPath == "/callback.php" || request.Url.LocalPath == $"{extPort}/callback.php" || request.Url.LocalPath == $"{port}/callback.php")
                    {
                        var clientID = Settings.WebServerModule.CcpAppClientId;
                        var secret = Settings.WebServerModule.CcpAppSecret;

                        var prms = request.Url.Query.TrimStart('?').Split('&');
                        var code = prms[0].Split('=')[1];
                        var state = prms.Length > 1 ? prms[1].Split('=')[1] : null;

                        if (state != "12") return false;

                        //state = 12 && have code
                        var result = await WebAuthModule.GetCharacterIdFromCode(code, clientID, secret);
                        if (result == null)
                        {
                            await WebServerModule.WriteResponce(WebServerModule.GetAccessDeniedPage("Mail Module", LM.Get("accessDenied")), response);
                            return true;
                        }

                        await SQLHelper.SQLiteDataInsertOrUpdateTokens("", result[0], result[1]);
                        await WebServerModule.WriteResponce(File.ReadAllText(SettingsManager.FileTemplateMailAuthSuccess)
                            .Replace("{header}", "authTemplateHeader")
                            .Replace("{body}", LM.Get("mailAuthSuccessHeader"))
                            .Replace("{body2}", LM.Get("mailAuthSuccessBody"))
                            .Replace("{backText}", LM.Get("backText")), response
                        );
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(ex.Message, ex, Category);
            }

            return false;
        }

        public override async Task Run(object prm)
        {
            if (IsRunning) return;
            IsRunning = true;
            try
            {
                if((DateTime.Now - _lastCheckTime).TotalMinutes < _checkInterval) return;
                _lastCheckTime = DateTime.Now;
                await LogHelper.LogModule("Running Mail module check...", Category);

                foreach(var groupPair in Settings.MailModule.AuthGroups)
                {
                    var group = groupPair.Value;
                    var terms = group.Labels;
                    if(group.Channel == 0)
                        continue;
                    var channel = group.Channel;

                    if (group.Id == 0 || terms == null || terms.Count == 0) continue;
                    var charId = group.Id;

                    var rToken = await SQLHelper.SQLiteDataQuery<string>("refreshTokens", "mail", "id", charId);
                    if (string.IsNullOrEmpty(rToken))
                    {
                        continue;
                    }

                    var token = await APIHelper.ESIAPI.RefreshToken(rToken, Settings.WebServerModule.CcpAppClientId, Settings.WebServerModule.CcpAppSecret);
                    if (string.IsNullOrEmpty(rToken))
                    {
                        await LogHelper.LogWarning("Unable to get correct token using refresh token! Refresh token might be expired!", Category);
                        continue;
                    }

                    var lastMailId = await SQLHelper.SQLiteDataQuery<int>("mail", "mailId", "id", group.Id.ToString());
                    var prevMailId = lastMailId;
                    var labelsData= await APIHelper.ESIAPI.GetMailLabels(Reason, group.Id.ToString(), token);
                    if(labelsData == null) continue;
                    var searchLabels = labelsData.labels.Where(a => a.name.ToLower() != "sent" && a.name.ToLower() != "received").ToList();
                    var senders = group.Senders;
                    var includePrivate = group.IncludePrivateMail;

                    if (senders.Count == 0 && (searchLabels.Count == 0 || terms.Count == 0))
                    {
                        await LogHelper.LogWarning($"Mail feed for user {group.Id} has no labels and senders configured or user has no required labels in-game!", Category);
                        continue;
                    }

                    var labelIds = labelsData.labels.Where(a=> terms.Contains(a.name)).Select(a => a.label_id).ToList();
                    var mails = (await APIHelper.ESIAPI.GetMailHeaders(Reason, group.Id.ToString(), token, 0, labelIds, senders.ToArray()));

                    if (lastMailId > 0)
                        mails = mails.Where(a => a.mail_id > lastMailId).OrderBy(a=> a.mail_id).ToList();
                    else
                    {
                        lastMailId = mails.LastOrDefault()?.mail_id ?? 0;
                        mails.Clear();
                    }

                    foreach (var mailHeader in mails)
                    {
                        if(mailHeader.mail_id <= lastMailId) continue;
                        if(!includePrivate && mailHeader.recipients.Count(a => a.recipient_id == charId) > 0) continue;

                        var mail = await APIHelper.ESIAPI.GetMail(Reason, group.Id.ToString(), token, mailHeader.mail_id);
                        var labelNames = string.Join(",", mail.labels.Select(a => searchLabels.FirstOrDefault(l => l.label_id == a)?.name)).Trim(',');
                        lastMailId = mailHeader.mail_id;

                        await SendMailNotification(channel, mail, labelNames);

                    }
                    if(prevMailId != lastMailId || lastMailId == 0)
                        await SQLHelper.SQLiteDataInsertOrUpdate("mail", new Dictionary<string, object>{{"id", group.Id.ToString()}, {"mailId", lastMailId}});
                }
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

        private async Task SendMailNotification(ulong channel, JsonClasses.Mail mail, string labelNames)
        {
            var sender = await APIHelper.ESIAPI.GetCharacterData(Reason, mail.from);

            var labels = string.IsNullOrEmpty(labelNames) ? null : $"{LM.Get("mailLabels")}  {labelNames} | ";

            var stamp = DateTime.Parse(mail.timestamp).ToString(Settings.Config.ShortTimeFormat);
            var embed = new EmbedBuilder()
                .WithThumbnailUrl(Settings.Resources.ImgMail)
                .AddField($"{LM.Get("mailSubject")} {mail.subject}",  await PrepareBodyMessage(mail.body))
                .WithFooter($"{labels}{LM.Get("mailDate")} {stamp}");
            var ch = APIHelper.DiscordAPI.GetChannel(channel);
            await APIHelper.DiscordAPI.SendMessageAsync(ch, LM.Get("mailMsgTitle", sender?.name), embed.Build()).ConfigureAwait(false);
        }

        public static async Task<string> PrepareBodyMessage(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var body = input.Replace("<br>", Environment.NewLine)
                .Replace("<b>", "**").Replace("</b>", "**")
                .Replace("<u>", "__").Replace("</u>", "__")
                .Replace("</font>", null)
                .Replace("<loc>", null).Replace("</loc>", null);

            try
            {
                while (true)
                {
                    var index = body.IndexOf("<font");
                    if (index == -1) break;
                    var lst = body.IndexOf('>', index + 1);
                    if (lst == -1) break;
                    body = index != 0 ? $"{body.Substring(0, index)}{body.Substring(lst + 1, body.Length - lst - 1)}" : $"{body.Substring(lst + 1, body.Length - lst - 1)}";
                }

                while (true)
                {
                    var index = body.IndexOf("<a");
                    if (index == -1) break;
                    var urlStart = body.IndexOf('\"', index) + 1;
                    var urlEnd = body.IndexOf('\"', urlStart) - 1;
                    var lst = body.IndexOf('>', index + 1);
                    var endTagStart = body.IndexOf('<', lst + 1);

                    var url = body.Substring(urlStart, urlEnd - urlStart + 1);
                    var text = body.Substring(lst + 1, endTagStart - lst - 1);

                    body = index != 0
                        ? $"{body.Substring(0, index)}{text}({url}){body.Substring(endTagStart, body.Length - endTagStart)}"
                        : $"{text}({url}){body.Substring(endTagStart, body.Length - endTagStart)}";
                }

                body = body.Replace("</a>", null);
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(ex.Message, ex, LogCat.Mail);
            }

            return body;
        }
    }
}
