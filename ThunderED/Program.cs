﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ThunderED.Classes;
using ThunderED.Helpers;
using ThunderED.Modules;

namespace ThunderED
{
    internal partial class Program
    {
        private static Timer _timer;

        private static void Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;


            if (!File.Exists(SettingsManager.FileSettingsPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Please make sure you have settings.json file in bot folder! Create it and fill with correct settings.");
                try
                {
                    Console.ReadKey();
                }
                catch
                {
                    // ignored
                }
                return;
            }

            //load settings
            var result = SettingsManager.Prepare();
            if (!string.IsNullOrEmpty(result))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(result);
                try
                {
                    Console.ReadKey();
                }
                catch
                {
                    // ignored
                }

                return;
            }

            LogHelper.LogInfo($"ThunderED v{VERSION} is running!").GetAwaiter().GetResult();
            //load database provider
            var rs = SQLHelper.LoadProvider();
            if (!string.IsNullOrEmpty(rs))
            {
                Console.BackgroundColor = ConsoleColor.Red;
                Console.WriteLine(result);
                try
                {
                    Console.ReadKey();
                }
                catch
                {
                    // ignored
                }
                return;
            }
            //update config settings
            if (SettingsManager.Settings.Config.ModuleNotificationFeed)
            {
                var dateStr = SQLHelper.SQLiteDataQuery<string>("cacheData", "data", "name", "nextNotificationCheck").GetAwaiter().GetResult();
                if(DateTime.TryParseExact(dateStr, new [] {"dd.MM.yyyy HH:mm:ss", $"{CultureInfo.InvariantCulture.DateTimeFormat.ShortDatePattern} {CultureInfo.InvariantCulture.DateTimeFormat.LongTimePattern}"}, CultureInfo.InvariantCulture.DateTimeFormat, DateTimeStyles.None, out var x))
                    SettingsManager.NextNotificationCheck = x;
            }

            //load language
            LM.Load().GetAwaiter().GetResult();
            //load APIs
            APIHelper.Prepare().GetAwaiter().GetResult();

            while (!APIHelper.DiscordAPI.IsAvailable)
            {
                Task.Delay(10).GetAwaiter().GetResult();
            }

            if (APIHelper.DiscordAPI.GetGuild() == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[CRITICAL] DiscordGuildId - Discord guild not found!");
                try
                {
                    Console.ReadKey();
                }
                catch
                {
                    // ignored
                }
                return;
            }

           /* var list = APIHelper.ESIAPI.GetFWSystemStats("1").GetAwaiter().GetResult();
            var caldari = list.Where(a => a.occupier_faction_id == 500001).Sum(a => a.victory_points);
            var gallente = list.Where(a => a.occupier_faction_id == 500004).Sum(a => a.victory_points);
            var cSysCount = list.Count(a => a.occupier_faction_id == 500001);
            var gSysCount = list.Count(a => a.occupier_faction_id == 500004);*/

            //Load modules
            TickManager.LoadModules();
            //initiate core timer
            _timer = new Timer(TickManager.Tick, new AutoResetEvent(true), 100, 100);

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = false; 
                _timer.Dispose();
                APIHelper.DiscordAPI.Stop();
            };

            while (true)
            {
                var command = Console.ReadLine();
                var arr = command?.Split(" ");
                if((arr?.Length ?? 0) == 0) continue;
                switch (arr[0])
                {
                    case "quit":
                        Console.WriteLine("Quitting...");
                        _timer.Dispose();
                        APIHelper.DiscordAPI.Stop();
                        return;
                    case "flushn":
                        Console.WriteLine("Flushing all notifications DB list");
                        SQLHelper.RunCommand("delete from notificationsList").GetAwaiter().GetResult();
                        break;
                    case "flushcache":
                        Console.WriteLine("Flushing all cache from DB");
                        SQLHelper.RunCommand("delete from cache").GetAwaiter().GetResult();
                        break;
                    case "help":
                        Console.WriteLine("List of available commands:");
                        Console.WriteLine(" quit    - quit app");
                        Console.WriteLine(" flushn  - flush all notification IDs from database");
                        Console.WriteLine(" getnurl - display notification auth url");
                        Console.WriteLine(" flushcache - flush all cache from database");
                        Console.WriteLine(" token [ID] - refresh and display EVE character token from database");
                        break;
                    case "token":
                        if(arr.Length == 1) continue;
                        if(!long.TryParse(arr[1], out var id))
                            continue;
                        var rToken = SQLHelper.SQLiteDataQuery<string>("refreshTokens", "token", "id", id).GetAwaiter().GetResult();
                        Console.WriteLine(APIHelper.ESIAPI.RefreshToken(rToken, SettingsManager.Settings.WebServerModule.CcpAppClientId, SettingsManager.Settings.WebServerModule.CcpAppSecret).GetAwaiter().GetResult());
                        break;
                }
                Thread.Sleep(10);
            }
        }


    }
}
