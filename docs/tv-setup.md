# Installing the Çevirgen TV app

The Çevirgen TV app is for **Android TV / Google TV** and is not in a store. Turn on debugging on your TV and Çevirgen
on your computer installs the TV app over the network. Debugging is free and does not harm the TV. The desktop app
shows these steps in its TV step and waits for the TV. The [Turkish guide](tv-kurulum.md) has more detail.

| TV | Status |
|---|---|
| **Android TV / Google TV** (Vestel, Arçelik, Beko, Grundig, Philips, Sony, TCL and others) | Tested on a real TV. Çevirgen installs it over the network |
| **LG** (webOS), **Samsung** (Tizen) | Çevirgen releases do not include packages for these TVs. Show the subtitles on the computer screen or on an Android TV box instead |
| **Linux models of Vestel, Arçelik, Beko and Grundig, Philips Saphi and Titan OS** | Not supported: there is no way to install apps on them |

The computer and the TV must be on the same home network.

## Android TV / Google TV

Tested on a real TV. Some TVs may not give the HDMI picture to other apps: subtitles appear but the picture stays
black. Please report your TV model in [Issues](https://github.com/ozguradmin/cevirgen/issues).

1. In **Settings › Device Preferences › About** (Google TV: **Settings › System › About**) press OK on **Build** 7 times.
2. In **Developer options** turn on **USB debugging** (and **Network debugging** if you see it).
3. In the TV step of Çevirgen, select **Android TV / Google TV**. Çevirgen finds the TV on the network.
4. The TV asks whether to allow debugging from this computer: tick **Always allow** and choose **Allow**. Çevirgen installs
   the app and opens it.
5. If there is no picture, press OK for the panel and pick the HDMI input with left and right.

If Çevirgen cannot find the TV, copy `cevirgen-androidtv_<version>.apk` from the `tv-app` folder of the installed app
(`%LOCALAPPDATA%\CevirgenApp\current\tv-app\`) to a USB stick and install it with a file manager on the TV.
