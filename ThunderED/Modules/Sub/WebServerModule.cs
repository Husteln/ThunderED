﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ThunderED.Classes;
using ThunderED.Helpers;
using ThunderED.Modules.OnDemand;

namespace ThunderED.Modules.Sub
{
    public class WebServerModule: AppModuleBase, IDisposable
    {
        private static System.Net.Http.HttpListener _listener;
        public override LogCat Category => LogCat.WebServer;

        public static Dictionary<string, Func<HttpListenerRequestEventArgs, Task<bool>>> ModuleConnectors { get; } = new Dictionary<string, Func<HttpListenerRequestEventArgs, Task<bool>>>();

        public WebServerModule()
        {
            LogHelper.LogModule("Inititalizing WebServer module...", Category).GetAwaiter().GetResult();
            ModuleConnectors.Clear();
        }

        public override async Task Run(object prm)
        {
            if(!Settings.Config.ModuleWebServer) return;

            if (_listener == null || !_listener.IsListening)
            {
                await LogHelper.LogInfo("Starting Web Server", Category);
                _listener?.Dispose();
                var port = Settings.WebServerModule.WebListenPort;
                var extPort = Settings.WebServerModule.WebExternalPort;
                var ip = Settings.WebServerModule.WebListenIP;
                _listener = new System.Net.Http.HttpListener(IPAddress.Parse(ip), port);
                _listener.Request += async (sender, context) =>
                {
                    try
                    {
                        var request = context.Request;
                        var response = context.Response;

                        if (request.Url.LocalPath.EndsWith(".js") || request.Url.LocalPath.EndsWith(".less") || request.Url.LocalPath.EndsWith(".css"))
                        {
                            var path = Path.Combine(SettingsManager.RootDirectory, "Content", "scripts", Path.GetFileName(request.Url.LocalPath));
                            if (request.Url.LocalPath.Contains("moments"))
                            {
                                path = Path.Combine(SettingsManager.RootDirectory, "Content", "scripts", "moments", Path.GetFileName(request.Url.LocalPath));
                            }

                            if (!File.Exists(path))
                                return;
                            if (request.Url.LocalPath.EndsWith(".less") || request.Url.LocalPath.EndsWith(".css"))
                                response.Headers.ContentType.Add("text/css");
                            if (request.Url.LocalPath.EndsWith(".js"))
                                response.Headers.ContentType.Add("text/javascript");

                            await response.WriteContentAsync(File.ReadAllText(path));

                            return;
                        }

                        if (request.Url.LocalPath == "/favicon.ico")
                        {
                            var path = Path.Combine(SettingsManager.RootDirectory, Path.GetFileName(request.Url.LocalPath));
                            if (!File.Exists(path))
                                return;
                            await response.WriteContentAsync(File.ReadAllText(path));
                            return;
                        }

                        if (request.Url.LocalPath == "/" || request.Url.LocalPath == $"{port}/" || request.Url.LocalPath == $"{extPort}/")
                        {
                            var extIp = Settings.WebServerModule.WebExternalIP;
                            var authUrl = $"http://{extIp}:{extPort}/auth.php";

                            var text = File.ReadAllText(SettingsManager.FileTemplateMain)
                                    .Replace("{header}", LM.Get("authTemplateHeader"))
                                    .Replace("{webAuthHeader}", LM.Get("webAuthHeader"))
                                    .Replace("{webWelcomeHeader}", LM.Get("webWelcomeHeader"));

                            //auth controls
                            var authText = string.Empty;
                            //auth
                            if (SettingsManager.Settings.Config.ModuleAuthWeb)
                            {
                                var groups = SettingsManager.Settings.WebAuthModule.AuthGroups.Where(a => a.Value.PreliminaryAuthMode || a.Value.ESICustomAuthRoles.Any()).ToList();
                                //no default auth if there are no default groups
                                if (groups.Count != SettingsManager.Settings.WebAuthModule.AuthGroups.Count)
                                    authText = $"<a href=\"{authUrl}\" class=\"btn btn-info btn-block {(!Settings.Config.ModuleAuthWeb ? "disabled" : "")}\" role=\"button\">{LM.Get("authButtonDiscordText")}</a>";

                                foreach (var @group in groups)
                                {
                                    var customAuthString = GetCustomAuthUrl(group.Value.ESICustomAuthRoles, group.Key);
                                    var bText = group.Value.CustomButtonText ?? $"{LM.Get("authButtonDiscordText")} - {group.Key}";
                                    authText += $"\n<a href=\"{customAuthString}\" class=\"btn btn-info btn-block\" role=\"button\">{bText}</a>";
                                }
                            }

                            //notifications
                            if (Settings.Config.ModuleNotificationFeed)
                            {
                                var authNurl = GetAuthNotifyURL();
                                authText += $"\n<a href=\"{authNurl}\" class=\"btn btn-info btn-block\" role=\"button\">{LM.Get("authButtonNotifyText")}</a>";
                            }
                            //mail
                            if (Settings.Config.ModuleMail)
                            {
                                var authNurl = GetMailAuthURL();
                                authText += $"\n<a href=\"{authNurl}\" class=\"btn btn-info btn-block\" role=\"button\">{LM.Get("authButtonMailText")}</a>";
                            }
                            text = text.Replace("{authControls}", authText);

                            //managecontrols
                            var manageText = string.Empty;
                            //timers
                            if (Settings.Config.ModuleTimers)
                            {
                                var authNurl = GetTimersURL();
                                manageText += $"\n<a href=\"{authNurl}\" class=\"btn btn-info btn-block\" role=\"button\">{LM.Get("authButtonTimersText")}</a>";
                            }

                            if (Settings.Config.ModuleHRM)
                            {
                                var authNurl = GetHRMAuthURL();
                                manageText += $"\n<a href=\"{authNurl}\" class=\"btn btn-info btn-block\" role=\"button\">{LM.Get("authButtonHRMText")}</a>";
                            }

                            text = text.Replace("{manageControls}", manageText);
                            await WriteResponce(text, response);

                            return;
                        }

                        var result = false;

                        foreach (var method in ModuleConnectors.Values)
                        {
                            try
                            {
                                result = await method(context);
                                if (result)
                                    break;
                            }
                            catch (Exception ex)
                            {
                                await LogHelper.LogEx($"Module method {method.Method.Name} throws ex!", ex, Category);
                            }
                        }

                        if (!result)
                        {
                            await WriteResponce(File.ReadAllText(SettingsManager.FileTemplateAuth3).Replace("{message}", "404 Not Found!")
                                .Replace("{header}", LM.Get("authTemplateHeader"))
                                .Replace("{body}", LM.Get("WebRequestUnexpected"))
                                .Replace("{backText}", LM.Get("backText")), response);
                        }
                    }
                    catch (Exception ex)
                    {
                        await LogHelper.LogEx(ex.Message, ex, Category);
                    }
                    finally
                    {
                        try
                        {
                            context.Response.Close();
                        }
                        catch
                        {
                            //ignore
                        }
                    }
                };
                _listener.Start();
            }
        }



        public static string GetAccessDeniedPage(string header, string message, string description = null)
        {
            return File.ReadAllText(SettingsManager.FileTemplateAuth3)
                .Replace("{message}", message)
                .Replace("{header}", header)
                .Replace("{header2}", header)
                .Replace("{description}", description)
                .Replace("{backText}", LM.Get("backText"));
        }

        public static async Task WriteResponce(string message, System.Net.Http.HttpListenerResponse response)
        {
            response.Headers.ContentEncoding.Add("utf-8");
            response.Headers.ContentType.Add("text/html;charset=utf-8");
            await response.WriteContentAsync(message);
        }

        public static string GetWebSiteUrl()
        {
            var extIp = SettingsManager.Settings.WebServerModule.WebExternalIP;
            var extPort = SettingsManager.Settings.WebServerModule.WebExternalPort;
            return  $"http://{extIp}:{extPort}";
        }

        
        public static string GetAuthNotifyURL()
        {
            var clientID = SettingsManager.Settings.WebServerModule.CcpAppClientId;
            var extIp = SettingsManager.Settings.WebServerModule.WebExternalIP;
            var extPort = SettingsManager.Settings.WebServerModule.WebExternalPort;
            var callbackurl =  $"http://{extIp}:{extPort}/callback.php";
            return $"https://login.eveonline.com/oauth/authorize/?response_type=code&redirect_uri={callbackurl}&client_id={clientID}&scope=esi-characters.read_notifications.v1+esi-universe.read_structures.v1+esi-characters.read_chat_channels.v1&state=9";
        }

        public static string GetTimersAuthURL()
        {
            var clientID = SettingsManager.Settings.WebServerModule.CcpAppClientId;
            var extIp = SettingsManager.Settings.WebServerModule.WebExternalIP;
            var extPort = SettingsManager.Settings.WebServerModule.WebExternalPort;
            var callbackurl =  $"http://{extIp}:{extPort}/callback.php";
            return $"https://login.eveonline.com/oauth/authorize?response_type=code&redirect_uri={callbackurl}&client_id={clientID}&state=11";
        }

        public static string GetMailAuthURL()
        {
            var clientID = SettingsManager.Settings.WebServerModule.CcpAppClientId;
            var extIp = SettingsManager.Settings.WebServerModule.WebExternalIP;
            var extPort = SettingsManager.Settings.WebServerModule.WebExternalPort;
            var callbackurl =  $"http://{extIp}:{extPort}/callback.php";
            return $"https://login.eveonline.com/oauth/authorize?response_type=code&redirect_uri={callbackurl}&client_id={clientID}&scope=esi-mail.read_mail.v1+esi-mail.send_mail.v1+esi-mail.organize_mail.v1&state=12";
        }

        internal static string GetCustomAuthUrl(List<string> permissions, string group = null)
        {
            var clientID = SettingsManager.Settings.WebServerModule.CcpAppClientId;
            var extIp = SettingsManager.Settings.WebServerModule.WebExternalIP;
            var extPort = SettingsManager.Settings.WebServerModule.WebExternalPort;
            var callbackurl =  $"http://{extIp}:{extPort}/callback.php";

            var grp = string.IsNullOrEmpty(group) ? null : $"&state=x{group}";

            var pString = string.Join('+', permissions);
            return $"https://login.eveonline.com/oauth/authorize?response_type=code&redirect_uri={callbackurl}&client_id={clientID}&scope={pString}{grp}";
        }


        public static string GetTimersURL()
        {
            var extIp = SettingsManager.Settings.WebServerModule.WebExternalIP;
            var extPort = SettingsManager.Settings.WebServerModule.WebExternalPort;
            return $"http://{extIp}:{extPort}/timers.php";
        }



        
        public static string GetHRMAuthURL()
        {
            var clientID = SettingsManager.Settings.WebServerModule.CcpAppClientId;
            var extIp = SettingsManager.Settings.WebServerModule.WebExternalIP;
            var extPort = SettingsManager.Settings.WebServerModule.WebExternalPort;
            var callbackurl =  $"http://{extIp}:{extPort}/callback.php";
            return $"https://login.eveonline.com/oauth/authorize?response_type=code&redirect_uri={callbackurl}&client_id={clientID}&state=matahari";
        }

        public static string GetHRMInspectURL(int id, string authCode)
        {
            var extIp = SettingsManager.Settings.WebServerModule.WebExternalIP;
            var extPort = SettingsManager.Settings.WebServerModule.WebExternalPort;
            return $"http://{extIp}:{extPort}/hrm.php?data=inspect{id}&id={authCode}&state=matahari";
        }

        public static string GetHRMMainURL(string authCode)
        {
            var extIp = SettingsManager.Settings.WebServerModule.WebExternalIP;
            var extPort = SettingsManager.Settings.WebServerModule.WebExternalPort;
            return $"http://{extIp}:{extPort}/hrm.php?data=0&id={authCode}&state=matahari";
        }

        public static string GetHRM_AjaxMailURL(long mailId, long inspectCharId, string authCode)
        {
            return $"hrm.php?data=mail{mailId}_{inspectCharId}&id={authCode}&state=matahari";
        }

        
        public static string GetHRM_AjaxMailListURL(long inspectCharId, string authCode)
        {
            return $"hrm.php?data=maillist{inspectCharId}&id={authCode}&state=matahari&page=";
        }
        
        public static string GetHRM_AjaxTransactListURL(long inspectCharId, string authCode)
        {
            return $"hrm.php?data=transactlist{inspectCharId}&id={authCode}&state=matahari&page=";
        }

        public static string GetHRM_AjaxJournalListURL(long inspectCharId, string authCode)
        {
            return $"hrm.php?data=journallist{inspectCharId}&id={authCode}&state=matahari&page=";
        }
        
        public void Dispose()
        {
            ModuleConnectors.Clear();
        }

        public static string GetHRM_AjaxLysListURL(int inspectCharId, string authCode)
        {
            return $"hrm.php?data=lys{inspectCharId}&id={authCode}&state=matahari&page=";
        }

        public static string GetHRM_AjaxContractsListURL(int inspectCharId, string authCode)
        {
            return $"hrm.php?data=contracts{inspectCharId}&id={authCode}&state=matahari&page=";
        }

        public static string GetHRM_AjaxContactsListURL(int inspectCharId, string authCode)
        {
            return $"hrm.php?data=contacts{inspectCharId}&id={authCode}&state=matahari&page=";
        }
    }
}
