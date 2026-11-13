# The Saturn Collection [![Total Download Count](https://img.shields.io/github/downloads/shreddism/TheSaturnCollection/total.svg)](https://github.com/shreddism/TheSaturnCollection/releases)

### Most Recent Update

1.2.1.0 - Releases page will have update notes.

# Important

Recent developments have rendered a lot of what used to be in this README mostly obsolete. The README will see more changes as things solidify. See how to change filter ordering below.

# Filter Ordering
- Go to File > Save settings as...
- Save the file and remember its location (documents folder).
- Open your file browser and open the file. If you've never seen/edited a .json file before, install/use Visual Studio Code.
- Pre-transform and post-transform filters are ordered among themselves by the settings file. For example. the multifilter and Temporal Resampler are pre-transform (as most plugins are), so if you want to use these in Akatsuki's relax mode, you'll have to do this:
- Seen below is an example of the contents of a Filters section.
```
"Filters": [
      {
        "Path": "Plugin2.Filter",
        "Settings": [
          ...
        ],
        "Enable": true
      },
      {
        "Path": "Plugin1.Filter",
        "Settings": [
          ...
        ],
        "Enable": true
      }
    ],
```
- There should be a comma after every filter but the last, so you would flip these around and fix the commas.
```
"Filters": [
      {
        "Path": "Plugin1.Filter",
        "Settings": [
          ...
        ],
        "Enable": true
      },
      {
        "Path": "Plugin2.Filter",
        "Settings": [
          ...
        ],
        "Enable": true
      }
    ],
```
- Save the file with Ctrl+S, go to OTD, File > Load settings... > Load the file. Console output should mirror the settings file. Example:
```
[Wacom PTK-470:Info]	Pen Bindings: Adaptive Binding: Button 1, Adaptive Binding: Button 2, Adaptive Binding: Button 3
[Wacom PTK-470:Info]	Filter Settings Circular Approximate Equal Area Inverse
[Wacom PTK-470:Info]	Filter Settings Plugin 1: (setting1: 0)
[Wacom PTK-470:Info]	Filter Settings Plugin 2: (setting2: 0)
[Settings:Info]	Driver is enabled.
```
- Note how I just included a random Circular Area option. This does NOT apply first, as it's post-transform, so it must apply after the output mode's transformation of tablet coordinates to screen coordinates, which pre-transform filters always go before. Keep this in mind, and if you want to be sure, look for the position in a plugin's source code.
