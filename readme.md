# ThunderED - EVE Online Discord Bot
[Reach us on Discord](https://discord.gg/UsnY6UR)

[Check the FAQ about installation and settings!](https://github.com/panthernet/ThunderED/wiki)

![](https://ci.appveyor.com/api/projects/status/67i3q6v804sjyse6?svg=true)

### Support with ISK: send donations to [Duke Veldspar](https://zkillboard.com/character/96496243/) character. Thank you!

## Advantages
* .NET Core powered multi-platform support (Win, Linux, Mac, etc.)
* Standalone build with no additional software or framework requirements (Docker compatible)
* 100% ESI API
* Multiple language support
* The very own JackKnife inspection utility!
* Web templates and rich settings
* Effective caching logic for less network and memory load
* Highly customizable Message templates for killmails without any source-code modifications
* Modular design for new extensions such as templates, modules and DB support

## Effective Corp and Alliance Management
With this bot you will have tools to:
* Allow only authed ingame characters into your Discord group
* Use preliminary auth and inspect characters using JackKnife utility
* Receive important notifications from many alliances and corporations
* Know when your citadels are under attack or low on fuel
* Know when moon extraction is finished or moon chunk has been blown
* Know when your sov space is in danger or war has been declared
* Know when your region or constellation has Sansha Incursion
* Know who joins or leaves corporations in your alliance
* Synchronize important EVE mails with Discord
* Synchronize your ops in FleetUp to remind them in Discord
* Manage important structure or ops timers with in-time reminders
* Monitor recent killmails in your space
* Query any char or corp info including the cyno and danger probability
* Receive pings from external messengers like Jabber, IRC, Telegram

## Supported Modules
* Web Auth - authenticate EVE characters in Discord using built-in web server, use JackKnife utility to inspect them before you accept them into your community
* Auth Check - check users access rights and strip permissions when char leaves your corp or ally
* Live Kill Feed - feed live EVE killmails using ZKill RedisQ into multiple channels 
* Reliable Kill Feed - feed killmails for selected corps or alliances in a reliable way using ZKill API
* Radius Kill Feed - feed live killmails in a radius around selected systems, constellations and regions into multiple channels
* Notifications Feed - feed EVE notifications from different characters into multiple channels
* Char & corp search - fetch information about characters and corps using special commands
* EVE Time - get EVE Online time
* Price Check - check relevant prices on item in all major trade hubs using special commands
* Ally & Corp Stats - get KM stats for selected alliances or corporations by day, month or year
* FleetUP integration - announces and reminders for FleetUp ops
* Timers - built-in web server for important timers and events. Auto add timers for reinforced structure events!
* Mail Feeder - feed mail by labels from authenticated characters 
* Jabber relay - connect with jabbers for cross messaging support
* IRC relay - exchange messages between IRC & Discord channels
* Telegram relay - exchange messages between Telegram & Discord channels
* EVE Chat relay - feed EVE ingame chat messages into Discord channel by streaming from user local apps to ThunderED server
* Incursion notifications - receive notifications when specified regions or constellations has Sansha Incursions
* Nullsec campaign  - sovereignty contest campaign notifications about TCU/IHUB entosis occurs in specified regions or constellations
* FWStats - access to !fwstats command to display information about the FW faction along with the LP farming tips
* (?) Channel MOTD integration - could be done by request

## Build Requirements
* Visual Studio 2017 Community Edition
* .NET Core 2.1

## Pics
![Timers](http://dl3.joxi.net/drive/2018/05/06/0014/0683/967339/39/fcb3e030f2.jpg)

![Sample kill feed](http://dl3.joxi.net/drive/2018/05/04/0014/0683/967339/39/4605864a01.jpg)

![Notifications](http://dl3.joxi.net/drive/2018/05/04/0014/0683/967339/39/39e91a5fde.jpg)

![JackKnife - HRM](http://joxi.ru/gmv89dJsL6wnnA.jpg)
