# The Saturn Collection [![Total Download Count](https://img.shields.io/github/downloads/shreddism/TheSaturnCollection/total.svg)](https://github.com/shreddism/TheSaturnCollection/releases)


A set of filters which is planned to grow to include the current multifilter for all different types of users, as well as more niche optional plugins.

Formatting may be non-final.

Important: The multifilter used to be split into separate versions: one that did not interpolate and two that did. Only the non-interpolated version of the multifilter was allowed on Akatsuki's relax mode because certain interpolation settings (expected milliseconds per report override) could cause the cursor to go all over the place. Note that if interpolation is disabled, it just acts like the non-interpolated version, automatically disabling that value as well as prediction, so if this is applicable to you, you should disable it to be safe. Note that if you want to enable Temporal Resampler at the same time and have this act as expected, Temporal Resampler should apply **after** this filter. If you want to know/change filter ordering, go to the bottom of this readme.

### Most Recent Update

0.10.0 - Just go to the release page.

## Things You Should Probably Know

### Terminology

If you're reading this without much context, you'll come across the term "velocity racket." I made this up just now to describe an effect of distance-weight antichatter. For an example, use "Kuuube's CHATTER EXTERMINATOR SMOOTH" on 50 strength. It might look like it's just decreasing pen resolution, but it's doing that as an intended side effect. This is velocity racket, where velocity becomes 0 for a report then non-0 for the next, and then 0 for the next, oscillating, making movement choppier when it doesn't exactly need to.

Names of plugins that are mentioned once or twice are put in quotations, but you are expected to know filters that are mentioned multiple times without quotation marks. I'll trust your ability to read context since you're reading this.

"Interpolating" filters have a Frequency setting at the bottom and run, or "UpdateState", at that frequency, independent of tablet report by default. They update their data, or "ConsumeState", on tablet report. They may (Temporal Resampler) or may not (Devocub, Hawku) be able to be told to respond immediately on tablet report by calling UpdateState at the end of ConsumeState. These can also be called interpolators or asynchronous filters.

"Non-interpolating" filters don't have a Frequency setting and just run at the tablet's report rate.

"Pre-Transform" filters operate on raw tablet data, before the output mode, and work independently of the user's output mode. These are always applied before any "Post-Transform" filter regardless of console output because they apply before the transform/output mode, but among themselves their application order is based on the user settings file, accessible from File > Save settings as...

"Post-Transform" filters are applied on whatever data the output mode outputs. In Absolute Mode this just means a different set of coordinates, but Relative Mode's transform just outputs velocity, so any cursor-modifying Post Transform filter either runs the risk of tablet drift or just doesn't work at all. The ordering situation is similar.

### Expected Performance Impact Of Filters

There are often people that report a non-interpolating filter increasing their "ms" in osu!stable by the better part of a millisecond, and they report an interpolator increasing it by over 2 milliseconds. This should NEVER happen!
Use Process Lasso to separate the cores used for osu! and OTD. Cores 0-1 are the hot cores for background tasks, so disable them for both osu! and OTD. The last 2 or 4 (2 in most cases) cores should be used for OTD, and the rest in the middle go to osu!
I'm not sure if this is a coincidence, but maybe it's worth mentioning that both of the people I've seen with the worst issues with this had a 9800X3D.

You should expect any non-interpolating filter to add almost nothing to frame milliseconds, and you should expect an interpolator (that runs at a higher frequency) to have an extremely small footprint of 0.1-0.2 ms because of the impact of polling rate, not filter processing.

### Using Other Filters

In most cases, the multifilter should be the only non-transform cursor-modifying filter enabled. If using interpolation, it's ill-advised to use another interpolator.

This means something like "Hover Distance Limiter" is completely fine, because it doesn't modify the cursor position.

Other unrelated plugins like "Circular Area" are also completely fine, as it's just an extra transform,
and since it's a post-transform filter, it is always ordered after every pre-transform filter like this one regardless of what the console output says, so it won't mess with the data going into any pre-transform filter,
meaning this plugin is completely in the clear.

A "multifilter" replaces the function of multiple filters without having to worry about filter order or timing consideration.
This means that you want to enable ANYTHING along with a multifilter or interpolator in general, you will have to worry about filter order/timing consideration.
It'll function just fine if everything is set well, but internal workings/timing consideration may be unreliable based on filter order, which is currently kind of unpredictable.
Please consider attempting to be able to do more with less before resorting to overfiltering.

# Multifilter Settings
Settings tooltips will appear on hovering over a setting's textbox. This further breakdown is assuming that you have read them.

### Reverse EMA
Follow [these instructions](https://github.com/X9VoiD/VoiDPlugins/wiki/Reconstructor#optimal-configuration).

### Inner Radius
Self-explanatory. Always directionally separated. This uses a Radial Follow-like calculation.

### Smoothed Antichatter
This is a bit of a hybrid system that achieves expected behavior in most cases.

### Stock EMA Weight
EMA weight is how much the output goes from itself to the input, so 1 means nothing. Time is taken into account when wiring, so no issue.

### Separated Threshold Mult
Smoothed Antichatter sets a base value for how eager the output cursor is to go to the input position when moving fast. This multiplies that value.

### Accel Response Aggressiveness
Explained in the tooltip.

### Directional Antichatter Threshold
Explained in the tooltip.

### Area Scale
Self explanatory. Full area PTK-470 can work with 1. Full area CTL-472 can work with 0.5.

### X Modifier
Multiplies the X values that the internals of the filter work on and divides outputs by the value. Makes the entire filter screen-space (if done right)! Example: 2560x1440 display (16/9) and 90x60 area (3/2) makes (16/9) / (3/2), which appears to be 1.185185 repeating. This value would be used.

## Interpolation
The multifilter is an asynchronous filter, making it able to interpolate. 
However, it still catches reports whenever they are sent, and can pass them at any time.
If this is enabled, the filter acts as normal. If this is disabled, the filter just sends its filtered point at report-time
and ignores the frequency.

#### Prediction Ratio
Prediction puts the latest reverse-smoothed position into a Kalman filter, which spits out a point that it thinks will be next.
Based on Prediction Ratio, the point used for interpolation will go from the latest reverse-smoothed position to the Kalman filter's point.
At 0.0 this step is just foregone entirely. At 0.5 the point lands halfway between the latest reverse-smoothed position and the Kalman filter's point. At 1 only the Kalman filter's point is used.
On certain tablets, other methods like using the Trajectory function on directions and changes in direction are mixed in slightly to improve accuracy more.
This was actually the entire point of the now-deleted Velocity Interpolation multifilter existing
The point is fed into the 3 points to be used in interpolation.

#### Wire Mode
Enabling this does the same thing that enabling "extraFrames" does in Temporal Resampler.
In an interpolator, "ConsumeState" is called on tablet report, while "UpdateState" is called strictly at the set frequency. Wiring ConsumeState to UpdateState increases frequency by the tablet's report rate, but weirdly.
Update is called at the frequency while it is called at the report rate asynchronously. This leads to big differences in the time between updates.
For ANYTHING that comes after this, this becomes a problem ranging from nothing to large.
For interpolation, delta time is used anyway, so it's fine. For any sort of smoothing, without warning the smoothing, you're usually going to be covering the same exact distance in 0.1 milliseconds as you do in 1. Velocity racket may occur.
In any case, this is managed to the point that it isn't noticeable through the way smoothing works as well as time compensation.

#### Filter Mode
The filter mode dictates what is put through the filters: the points to be interpolated between or the interpolation's output.
Point filtering doesn't have to worry about time compensation in smoothing; it just runs at the report rate.
Disabling interpolation will force point filtering, as that's the only way it can work.

#### Expected Milliseconds Per Report Override
Interpolation and prediction uses timing averages of inconsistent integer millisecond report times to generally know what to do. I thought it would be reassuring to add a hard override for those who know their tablet's average. Doesn't take effect at 0.
Personally, I have a PTK-470 and I measured 3.302466 with the stock pen.

#### "Tablet-Specific Tweaks"
This looks up the name of the tablet in use and gives it an ID. Effects include tweaks to the Kalman filter used for prediction, as well as attempted bug mitigations for pressing/lifting.
The main source of position inaccuracy in the Kalman filter as seen in Temporal Resampler is change in direction, whether it be acceleration for a jump or turning for flow aim.
It seems to not react enough to change in direction itself, causing it to for example apply too low of a velocity when accelerating and apply too high of a velocity when decelerating. These two would cancel each other out, so there wouldn't be any massive egregious positional overshoot.
Still though, I never used it even though I knew that it had very low error on my tablet because I couldn't feel my deceleration the same.
Using tools that plotted out the live position error onto a graph, I found good dynamic values to simply just multiply however much the Kalman filter thought it should accelerate by in order for it to stop looking like a pattern under acceleration and more like noise. I also chose values for other parameters based on summed direction length error separated by if it was shorter or longer, as well as summed distance-squared position error.

I tested values on a PTK-470 and a CTL-480. My CTL-480 pen drops the PTK-470 down to 200hz, so I thought it would be better than nothing to use that for 200hz tablets. The Kalman filter parameters that work best differ wildly between the two tablets, however I haven't been able to make a clear improvement for the 200hz PTK-470 than just using the values from the 300hz tune. At the very least there's no obvious thing that's hurting it.

On Intuos Pro (200/300hz, 200lines/mm) tablets, using the Trajectory function to extrapolate the direction between reports themselves is reliable enough to give an estimate of the next direction in certain scenarios. This never completely overwrites the Kalman filter's prediction, as combining them gives less error than just trusting one or the other. This was the point of the Velocity Interpolation multifilter, but now that its purpose has been merged, it's now been deleted. This grants double-digit percent gains in accuracy and allows me to set parameters that favor stability over accel-responsiveness in the Kalman filter, as the trajectory methods would take over anyway if needed. The values on 300hz are different here than on 200hz.

PTK-x70 tablets are known (source: me) to give funny unreliable position reports on press/lift (which is a PRESSURE thing, not a TILT thing, to prevent misreads, and it's a HARDWARE feature that cannot be disabled in OTD) that messes up all prediction. The filter just smooths its output to filtered sensor position for a few reports after this out of necessity.
There are other Wacom tablets that apparently have similar behaviors, so they're given similar failsafes.

#### Frequency
On Windows, setting Frequency to anything but something that results in an integer-millisecond update interval (so 1000 or 500 in edge cases) will slam the CPU.
If your CPU can handle it, this will be fine, but system timing when it comes to the "Wire" setting may be inaccurate (untested).
Any reasonable frequencies work fine on Linux without increasing CPU, so you can just set it to 2x or 4x your display refresh rate without worry.
Support for uber-high frequencies should be decent.
If you can change frequencies anyhow, you can control filter frequency when using interp filtering. This can make the difference between accel response looking either natural or janky, depending on your display.

# Area Rounding
A sort of "Circular Area"-lite. You can make its effects as extreme or as subtle as you want with configuration. Most things are explained in the tooltips. The modifiers for input and output being different from each other allows you to set your own curve.

# Custom Reset Absolute Mode
You may need to apply and save multiple times for settings to apply properly.

Multiple options are available to move your area. These are pretty much self explanatory and many things can be done with a combination of these.

## Reset Modes

#### Set Tablet Area Center To Position
This effectively puts the cursor to the center of the display setting's area.

#### Drag Tablet Area / Fake Relative Mode
These internally do the same thing, but dragging holds the reset while fake relative mode just triggers it.

#### Set Both Centers To Posiiton
This sets the center of the display area's setting. which changes where setting the tablet area's center to position will put the cursor. This also changes behavior if using an extra transform like Circular Area.

#### Reset To Stock Settings
Self-explanatory. Might need multiple applies/saves to function.

# Filter Ordering
- Go to File > Save settings as...
- Save the file and remember its location (documents folder).
- Open your file browser and open the file. If you've never seen/edited a .json file before, install/use Visual Studio Code.
- Pre-transform and post-transform filters are ordered among themselves by the settings file. For example. the non-interpolating multifilter and Temporal Resampler are pre-transform (as most plugins are), so if you want to use these in Akatsuki's relax mode, you'll have to do this:
- Seen below is an example of the contents of a Filters section.
```
"Filters": [
      {
        "Path": "TemporalResampler",
        "Settings": [
          ...
        ],
        "Enable": true
      },
      {
        "Path": "Saturn.MultifilterU",
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
        "Path": "Saturn.MultifilterU",
        "Settings": [
          ...
        ],
        "Enable": true
      },
      {
        "Path": "TemporalResampler",
        "Settings": [
          {
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
[Wacom PTK-470:Info]	Filter Settings Saturn - Multifilter (Non-Interpolated): (reverseSmoothing: 1), (dacInner: 0), (dacOuter: 0), (rInner: 25), (stockWeight: 1), (smoothDist: 50), (sepMult: 1), (aResponse: 0), (areaScale: 0.5), (xMod: 1)
[Wacom PTK-470:Info]	Filter Settings Temporal Resampler: (followRadius: 0), (latency: 0), (reverseSmoothing: 1), (extraFrames: True), (loggingEnabled: False), (Frequency: 1000), (frameShift: 0)
[Settings:Info]	Driver is enabled.
```
- Note how I just included a random Circular Area option. This does NOT apply first, as it's post-transform, so it must apply after the output mode's transformation of tablet coordinates to screen coordinates, which pre-transform filters always go before. Keep this in mind, and if you want to be sure, look for the position in a plugin's source code.

# Filter Ordering
- Go to File > Save settings as...
- Save the file and remember its location (documents folder).
- Open your file browser and open the file. If you've never seen/edited a .json file before, install/use Visual Studio Code.
- Pre-transform and post-transform filters are ordered among themselves by the settings file. For example. the non-interpolating multifilter and Temporal Resampler are pre-transform (as most plugins are), so if you want to use these in Akatsuki's relax mode, you'll have to do this:
- Seen below is an example of the contents of a Filters section.
```
"Filters": [
      {
        "Path": "TemporalResampler",
        "Settings": [
          ...
        ],
        "Enable": true
      },
      {
        "Path": "Saturn.MultifilterU",
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
        "Path": "Saturn.MultifilterU",
        "Settings": [
          ...
        ],
        "Enable": true
      },
      {
        "Path": "TemporalResampler",
        "Settings": [
          {
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
[Wacom PTK-470:Info]	Filter Settings Saturn - Multifilter (Non-Interpolated): (reverseSmoothing: 1), (dacInner: 0), (dacOuter: 0), (rInner: 25), (stockWeight: 1), (smoothDist: 50), (sepMult: 1), (aResponse: 0), (areaScale: 0.5), (xMod: 1)
[Wacom PTK-470:Info]	Filter Settings Temporal Resampler: (followRadius: 0), (latency: 0), (reverseSmoothing: 1), (extraFrames: True), (loggingEnabled: False), (Frequency: 1000), (frameShift: 0)
[Settings:Info]	Driver is enabled.
```
- Note how I just included a random Circular Area option. This does NOT apply first, as it's post-transform, so it must apply after the output mode's transformation of tablet coordinates to screen coordinates, which pre-transform filters always go before. Keep this in mind, and if you want to be sure, look for the position in a plugin's source code.
