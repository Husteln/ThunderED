using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Newtonsoft.Json;
using ThunderED.Helpers;
using ThunderED.Json;
using ThunderED.Json.PriceChecks;

namespace ThunderED.Modules.Static
{
    internal class PriceCheckModule: AppModuleBase
    {
        public override LogCat Category => LogCat.PriceCheck;
        public override Task Run(object prm)
        {
            return Task.CompletedTask;
        }

        public static async Task Check(ICommandContext context, string command, string system)
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", SettingsManager.DefaultUserAgent);

                var regex = new Regex("(x[0-9]+$)|(^[[].+)");
                var value = regex.Replace(command, string.Empty);

                string[] lines = value.Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries
                );

                List<JsonClasses.SearchResult> result = new List<JsonClasses.SearchResult>();
                JsonClasses.SearchResult item = new JsonClasses.SearchResult();
                // List<JsonClasses.SearchName> itemNameResults = new List<JsonClasses.SearchName>();
                List<long> items = new List<long>();
                List<JsonClasses.SearchName> names = new List<JsonClasses.SearchName>();
                var token = await APIHelper.ESIAPI.GetSearchTokenString();
                for (int i = 0; i < lines.Length; i++)
                {
                    await LogHelper.LogDebug($"PC lines cycle {i}", LogCat.PriceCheck, true);
                    // if (!string.IsNullOrWhiteSpace(lines[i]))
                    // {
                        item = await APIHelper.ESIAPI.SearchTypeEntity("PriceCheck", lines[i], token);
                    // }
                     // result[i] = await APIHelper.ESIAPI.SearchTypeEntity("PriceCheck", value, token);

                    if (item.inventory_type.Count() == 0 && !string.IsNullOrWhiteSpace(lines[i]))
                        await APIHelper.DiscordAPI.ReplyMessageAsync(context, LM.Get("itemNotExist",lines[i]));
                    else if (item.inventory_type.Count() >= 1)
                    {
                        items.Add(item.inventory_type[0]);
                        try
                        {
                            var httpContent = new StringContent($"[{item.inventory_type[0]}]", Encoding.UTF8, "application/json");
                            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                            var itemName = await httpClient.PostAsync($"{SettingsManager.Settings.Config.ESIAddress}latest/universe/names/?datasource=tranquility", httpContent);

                            if (!itemName.IsSuccessStatusCode)
                            {
                                await APIHelper.DiscordAPI.ReplyMessageAsync(context, LM.Get("ESIFailure"));
                                await Task.CompletedTask;
                                itemName?.Dispose();
                                return;
                            }

                            var itemNameResult = await itemName.Content.ReadAsStringAsync();
                            var itemNameResults = JsonConvert.DeserializeObject<List<JsonClasses.SearchName>>(itemNameResult)[0];
                            names.Add(itemNameResults);
                            itemName?.Dispose();

                            // await GoFuzz(httpClient, context, system, item.inventory_type, itemNameResults);

                        }
                        catch (Exception ex)
                        {
                            await LogHelper.LogEx(ex.Message, ex, LogCat.PriceCheck);
                        }
                    }

                }
                await GoFuzz(httpClient, context, system, items, names);
            }                

            catch (Exception ex)
            {
                await APIHelper.DiscordAPI.ReplyMessageAsync(context, "ERROR Please inform Discord/Bot Owner");
                await LogHelper.LogEx(ex.Message, ex, LogCat.PriceCheck);
            }
        }

        private static async Task GoFuzz(HttpClient httpClient, ICommandContext context, string system,
            List<long> idList, List<JsonClasses.SearchName> itemNameResults)
        {
            var url = "https://market.fuzzwork.co.uk/aggregates/";

            var systemAddon = string.Empty;
            var systemTextAddon = string.IsNullOrEmpty(system) ? null : $"{LM.Get("fromSmall")} {system}";
            switch (system?.ToLower())
            {
                default:
                    systemAddon = "?station=60003760";
                    break;
                case "amarr":
                    systemAddon = "?station=60008494";
                    break;
                case "rens":
                    systemAddon = "?station=60004588";
                    break;
                case "dodixie":
                    systemAddon = "?station=60011866";
                    break;
            }

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent", SettingsManager.DefaultUserAgent);
            var webReply = await httpClient.GetStringAsync($"{url}{systemAddon}&types={string.Join(",", idList)}");
            var market = JsonConvert.DeserializeObject<Dictionary<string,JsonFuzz.FuzzItems>>(webReply);
            // var i = new int();
            // i = 0;
            await LogHelper.LogDebug($"PC Fuzz url: {url}{systemAddon}&types={string.Join(",", idList)}", LogCat.PriceCheck, true);
            await LogHelper.LogInfo($"Sending {context.Message.Author}'s Price check", LogCat.PriceCheck);
            var valuesnames = market.Zip(itemNameResults, (m,i) => Tuple.Create(m,i));
            foreach (var mi in valuesnames)
            {
                await LogHelper.LogDebug($"PC Fuzz cycle, {mi.Item2.name}", LogCat.PriceCheck, true);
                var builder = new EmbedBuilder()
                    .WithColor(new Color(0x00D000))
                    .WithThumbnailUrl($"https://image.eveonline.com/Type/{mi.Item2.id}_32.png")
                    // .WithAuthor(author =>
                    // {
                    //     author
                    //         .WithName($"{LM.Get("Item")}: {mi.Item2.name}")
                    //         .WithUrl($"https://www.fuzzwork.co.uk/info/?typeid={mi.Item2.id}/");
                    // })
                    .WithDescription($"{LM.Get("Prices")} {systemTextAddon}")
                    .AddField(
                        $"{LM.Get("Item")}: {mi.Item2.name}",
                        $"{LM.Get("Volume")}: {mi.Item1.Value.buy.volume} / {mi.Item1.Value.sell.volume:N0}",
                        true
                    )
                    .AddField(
                        $"{LM.Get("Buy")}: {LM.Get("marketHigh")}/{LM.Get("marketMid")}/{LM.Get("marketLow")}",
                        $"{mi.Item1.Value.buy.max} / {mi.Item1.Value.buy.weightedAverage} / {mi.Item1.Value.buy.min}",
                        true
                    )
                    .AddField(
                        $"{LM.Get("Sell")}: {LM.Get("marketHigh")}/{LM.Get("marketMid")}/{LM.Get("marketLow")}",
                        $"{mi.Item1.Value.sell.max} / {mi.Item1.Value.sell.weightedAverage} / {mi.Item1.Value.sell.min}",
                        true
                    )
                    .WithFooter($"https://market.fuzzwork.co.uk/station/{systemAddon}/type/{mi.Item2.id}")
                    ;
                    // .AddField(LM.Get("Buy"), $"{LM.Get("marketHigh")}: {mi.Item1.Value.buy.max:N2}{Environment.NewLine}" +
                    //                          $"{LM.Get("marketMid")}: {mi.Item1.Value.buy.weightedAverage:N2}{Environment.NewLine}" +
                    //                          $"{LM.Get("marketLow")}: {mi.Item1.Value.buy.min:N2}{Environment.NewLine}" +
                    //                          $"{LM.Get("Volume")}: {mi.Item1.Value.buy.volume}", true)
                    // .AddField(LM.Get("Sell"), $"{LM.Get("marketLow")}: {mi.Item1.Value.sell.min:N2}{Environment.NewLine}" +
                    //                           $"{LM.Get("marketMid")}: {mi.Item1.Value.sell.weightedAverage:N2}{Environment.NewLine}" +
                    //                           $"{LM.Get("marketHigh")}: {mi.Item1.Value.sell.max:N2}{Environment.NewLine}" +
                    //                           $"{LM.Get("Volume")}: {mi.Item1.Value.sell.volume:N0}", true);
                var embed = builder.Build();
                await APIHelper.DiscordAPI.ReplyMessageAsync(context, "", embed).ConfigureAwait(false);
                await Task.Delay(500);
            }
        }
    }
}
