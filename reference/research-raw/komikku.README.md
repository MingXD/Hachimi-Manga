# This is a personal fork of [Komikku](https://www.gitlab.com/valos/Komikku)

This is a fork of Komikku and NOT the official repo. It will almost definitely have issues. The `personal` branch is where I make my changes. Most of the time, both upstream and this branch are **essentially the same** because I try to upstream all the working changes asap and I have a pretty comfortable reading environment here now.

Basically, don't use this.

Major differences right now (in order of stability):

* Chapters are downloaded in the order that you selected them in the Chapters list and not based on chapter list sorting. Seems stable, especially since it simply adds a variable and that's basically it.
* Search within chapter lists, only accessible by beginning to type. There is no search button. The selection behavior during search works as I want and expect it to work, but it might not be ideal for everyone.
* MangaDex BBCode support in synopsis or manga description and possible HTML support in other sites.
* Experimental Tachiyomi json backup import functionality. Not sure it'll work for anyone else and even if it does, it's not assured to properly work at least. This form of backups has been deprecated by Tachiyomi, so it's pretty much useless now.

The actual README follows. It's mostly the same as the official README.

# <a href="https://valos.gitlab.io/Komikku/"><img height="88" src="data/icons/info.febvre.Komikku.svg" />Komikku</a>

[![pipeline status](https://gitlab.com/fushinari/Komikku/badges/personal/pipeline.svg)](https://gitlab.com/fushinari/Komikku/commits/personal)
[![Please do not theme this app](https://stopthemingmy.app/badge.svg)](https://stopthemingmy.app)
[![Donate using Liberapay](https://img.shields.io/liberapay/receives/valos.svg?logo=liberapay)](https://en.liberapay.com/valos/donate)

__Komikku__ is a manga reader for [GNOME](https://www.gnome.org). It focuses on providing a clean, intuitive and adaptive interface.

## License

__Komikku__ is licensed under the [GPLv3+](https://www.gnu.org/licenses/gpl-3.0.html).

## Keys features

* Online reading from dozens of servers
* Offline reading of downloaded comics
* Categories to organize your library
* RTL, LTR, Vertical and Webtoon reading modes
* Several types of navigation:
  * Keyboard arrow keys
  * Right and left navigation layout via mouse click or tapping (touchpad/touch screen)
  * Mouse wheel
  * 2-fingers swipe gesture (touchpad)
  * Swipe gesture (touch screen)
* Automatic update of comics
* Automatic download of new chapters
* Reading history
* Light and dark themes

## Screenshot

<img src="screenshots/library-dark.png" width="912">

## Installation

### Flatpak

<a href='https://flathub.org/apps/details/info.febvre.Komikku'><img width='240' alt='Download on Flathub' src='https://flathub.org/assets/badges/flathub-badge-en.png'/></a>

### Native package

__Komikku__ is available as native package in the repositories of the following distributions:

[![Packaging status](https://repology.org/badge/vertical-allrepos/komikku.svg)](https://repology.org/project/komikku/versions)

### Flatpak of development version

Setup [Flatpak](https://www.flatpak.org/setup/) for your Linux distro. Download the Komikku flatpak from the last passed [Gitlab pipeline](https://gitlab.com/valos/Komikku/pipelines). Then install the flatpak.

```bash
flatpak install info.febvre.Komikku.flatpak
```

## Building from source

### Option 1: Test or building a Flatpak with GNOME Builder

Open GNOME Builder, click the **Clone...** button, paste the repository url.

Clone the project and hit the **Play** button to start building Komikku or test Flatpaks with **Export Bundle** button.

### Option 2: Testing with Meson

Dependencies:

* `git`
* `ninja`
* `meson` >= 0.50.0
* `python` >= 3.8
* `gtk` >= 3.24.1
* `libhandy` >= 1.5.0
* `python-beautifulsoup4`
* `python-brotli`
* `python-cloudscraper`
* `python-dateparser`
* `python-keyring` >= 21.6.0
* `python-lxml`
* `python-magic` or `file-magic`
* `python-natsort`
* `python-pillow`
* `python-pure-protobuf`
* `python-unidecode`

This is the best practice to test __Komikku__ without installing using meson and ninja.

#### First time

```bash
git clone https://gitlab.com/valos/Komikku
make setup
make local
```

#### Later on

```bash
make local
```

#### To Run

```bash
make run
```

To use the development profile, use `make develop` instead of `make local`.

### Option 3: Build and install system-wide directly with Meson

**WARNING**: This approach is discouraged, since it will manually copy all the files in your system. **Uninstalling could be difficult and/or dangerous**.

But if you know what you're doing, here you go:

```bash
git clone https://gitlab.com/valos/Komikku
cd Komikku
make
make install
```

If you've already used Option 2, do:

```bash
make clean
make
make install
```

You may need to run `make install` with `sudo`.

## Code of Conduct

We follow the [GNOME Code of Conduct](/CODE_OF_CONDUCT.md).
All communications in project spaces are expected to follow it.

## Translations

Helping to translate __Komikku__ or add support to a new language is very welcome.

## Sponsor this project

You can help me to keep developing __Komikku__ through donations. Any amount will be greatly appreciated :-)

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/X8X06EM3L) [![lp Donate](https://liberapay.com/assets/widgets/donate.svg)](https://liberapay.com/valos/donate) [![PayPal](https://www.paypalobjects.com/en_US/i/btn/btn_donate_LG.gif)](https://www.paypal.com/donate?business=GSRGEQ78V97PU&no_recurring=0&item_name=You+can+help+me+to+keep+developing+apps+through+donations.&currency_code=EUR)

## Disclaimer

The developer of this application does not have any affiliation with the content providers available.
