
### YACReader
* Improve save current page. It includes the comic file name in the default image name and the page number.
* Add an extract page action. It extracts the original visible pages when image based variants are used. The file name and page number are used as the output.
* Save the destination path for save/export page between runs.
* Fix low resolution images generated on `save current page`.
* Fix magnifying glass not using the source material.
* Improve last page detection in continuous scroll mode.
* Improve What's new dialog to show the full changelog.
* Improve continuous scroll mode with better performance and less flickering when resizing/loading.
* Fix reading position being reset to the cover when changing between fit modes.

### YACReaderLibrary
* Fix comics order when opening a comic from YACReaderLibrary.

### YACReaderLibraryServer
* Don't crash at startup if the server can't bind to any address.
* Redirect `/` to `/webui`.
* New `/webui` design.
* Settings regarding metadata and auto-libraries updates can be controlled from `/webui/settings`.
* Add a new web content browser to `/webui`.
* Add update libraries functionality to `/webui`.
* Fix `number` in various HTTP APIs.

### All GUI Apps
* Better system theme detection.
* New button in the help dialog for showing the changelog.

### All apps
* Add support for long paths on Windows. This is experimental and only includes library creation and opening files (including the server), other functionality may or may not work with long paths, but the critical aspect of this is the folder structure of the library so overall it is a big win. NOTE: You need to enable long paths support on Windows itself for this to work, `HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem` -> `LongPathsEnabled` -> `1`.
* Better PDF rendering that accounts for tall images used in web comics.