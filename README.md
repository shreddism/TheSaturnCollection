# The Saturn Collection [![Total Download Count](https://img.shields.io/github/downloads/shreddism/TheSaturnCollection/total.svg)](https://github.com/shreddism/TheSaturnCollection/releases)
 
A set of filters which is planned to grow to include the current multifilter for all different types of users, as well as more niche optional plugins.

Formatting may be non-final.

### Most Recent Update + Known Issues + Fix Status

0.7.0 - Slight behavior changes, edge case fixes, removed a nonfunctional setting.

There's an issue with aspect ratio compensation that makes the cursor act odd when X Modifier is not set to 1. If used normally, as in being above 1, there's not much to be actively worried about; it's just slight underaim and latency. It shouldn't be set below 1 in 0.7.0.

Both interpolation methods have a barely noticeable velocity racket when Wire is enabled because of a time compensation error.

Due to an oversight, setting stock weight to 1, accel response aggressiveness to 0, AND *additional antichatter* to 0 (all three exact) just makes the filter not work. There's no good reason in existence to do this, though.

If you are reading this right now, 0.9.0 is awaiting to be merged. It fixes these issues among a lot of other things.

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

In most cases, the multifilter should be the only non-transform cursor-modifying filter enabled.

This means something like "Hover Distance Limiter" is completely fine, because it doesn't modify a cursor position.
In fact, a lot of issues when taking the pen away from the tablet on certain tablets (Wacom PTK-x70) when using certain filters are fixed by using "Hover Distance Limiter" and leaving everything to default except checking the "Use Near Proximity Cutoff" setting.

Other unrelated plugins like "Circular Area" are also completely fine, as it's just an extra transform,
and since it's a post-transform filter, it is always ordered after every pre-transform filter like this one regardless of what the console output says, so it won't mess with the data going into any pre-transform filter,
meaning this plugin is completely in the clear.

A "multifilter" replaces the function of multiple filters without having to worry about filter order or timing consideration.
This means that you want to enable ANYTHING along with a multifilter (including another multifilter), you will have to worry about filter order/timing consideration.
It'll function just fine if everything is set well, but internal workings/timing consideration may be unreliable based on filter order, which is currently kind of unpredictable.
Please consider attempting to be able to do more with less before resorting to overfiltering.

Some combinations currently make perfect sense. If the version is 0.8.0 and you are on absolute mode (this does not work on relative), you can massively reduce Additional Antichatter and use Radial Follow (Screen Space) with only an outer radius to preference.

# Multifilter Settings

Settings tooltips will appear on hovering over a setting's textbox. This further breakdown is assuming that you have read them.

## Method-exclusive Settings

### Position Interpolation

People gloss over tooltips (Please hover over a settings textbox if confused!) all the time so this is a last ditch effort at catching those people.
You're probably going to want to use this one, or the non-interpolated version if your tablet hz lines up so well with display hz that you can't tell a difference.

#### Prediction Ratio

Temporal Resampler puts the latest reverse-smoothed position into a Kalman filter, which spits out a point that it thinks will be next.
Based on Prediction Ratio, the point used for interpolation will go from the latest reverse-smoothed position to the Kalman filter's point.
At 0.0 this step is just foregone entirely. At 0.5 the point lands halfway between the latest reverse-smoothed position and the Kalman filter's point. At 1 only the Kalman filter's point is used.
The point is fed into the 3 points to be used in interpolation.

#### Wire - Filter Mode
Wiring is addressed in a section below. The filter mode dictates what is put through the filters: the points to be interpolated between or the interpolation's output.
Point filtering doesn't have to worry about time compensation in smoothing; it just runs at the report rate.

### Velocity Interpolation

Just as another heads-up, this filter's inaccuracy scales very strongly with tablet noise/lower report rate.
I can say this functions with a PTK-x70, and perhaps a PTH-x60, but the concept of a velocity filter is novel, and this specific filter really trusts the tablet.
For reliability's sake, you may want to use the Position Interpolation multifilter, as again, that uses Temporal Resampler's interpolation method.

#### Velocity Trajectory Limiter 
The trajectory estimator from Temporal Resampler is used, but on per-report change in position, or velocity.
This ended up having the capability to extrapolate decently well if manual checks were put in place to reduce error. 
The setting goes from 2 to 3 because of it's internal working and I left it intentionally to leave an obvious difference between this and Position Interpolation.
The Kalman filter's output naturally being incongruent scared me off using it when I didn't fully get it because of the importance of 0, even though there are obvious fixes.
Because this method specifically is more of a pet project, changing this is low-priority.

#### Accel/Decel Adjustment 
Because this specific method is a velocity filter, it naturally has tablet drift if it isn't watched. There's automatic slight absolute correction every refresh. 

Accel Adjustment at 0 does nothing while having it at 1 will make simple correction 2x as aggressive on acceleration and 0x as aggressive on deceleration.
This was considered useful because of behavior analysis.

Decel Adjustment is more straightforward; it ensures no bad overshoot by making safe correction more aggressive on deceleration.

## Interpolation-exclusive Settings

#### Wire
In an interpolator, "ConsumeState" is called on tablet report, while "UpdateState" is called strictly at the set frequency. Wiring ConsumeState to UpdateState increases frequency by the tablet's report rate, but weirdly.
Update is called at the frequency while it is called at the report rate asynchronously. This leads to big differences in the time between updates.
For ANYTHING that comes after this, this becomes a problem ranging from nothing to large.
For interpolation, time is used anyway, so it's fine. For any sort of smoothing, without warning the smoothing, you're usually going to be covering the same exact distance in 0.1 milliseconds as you do in 1. Velocity racket may occur.
This is managed to the point that it isn't noticeable through the way smoothing works as well as time compensation.

#### Expected Milliseconds Per Report Override
Interpolation uses timing averages of inconsistent integer millisecond report times to generally know what to do. I thought it would be reassuring to add a hard override for those who know their tablet's average. Doesn't take effect at 0.

#### "Wacom PTK-x70 Series Toggle"
This is said in the tooltip, but this may apply to people with PTH-x60 tablets as well, it's just not been tested/confirmed yet.
These tablets are known (source: me) to give funny unreliable position reports on press/lift (which is a PRESSURE thing, not a TILT thing, to prevent misreads, and it's a HARDWARE feature that cannot be disabled in OTD) that mess up all prediction.
This sticks a control rod in what could be a prediction disaster. In Velocity Interpolation, this also modifies correction to be better IF your tablet is trustworthy.

#### Frequency
Yes, Frequency. This section is carved out to point out to anyone unaware that on Windows, setting Frequency to anything but something that results in an integer-millisecond update interval (so 1000 or 500 in edge cases) will slam the CPU.
If your CPU can handle it, this will be fine, but system timing when it comes to the "Wire" setting may be inaccurate (untested). All frequencies work fine on Linux, so you can just set it to 1x or 2x your display refresh rate without worry.
Support for uber-high frequencies should be decent.

## Other Settings

#### Reverse EMA
Follow [these instructions](https://github.com/X9VoiD/VoiDPlugins/wiki/Reconstructor#optimal-configuration).

#### Directional Antichatter
Explained in the tooltip.

#### Inner Radius
Self-explanatory. Always directionally separated. This uses a Radial Follow-like calculation.

#### Stock EMA Weight
EMA weight is how much the output goes from itself to the input, so 1 means nothing. Time is taken into account when wiring, so no issue.

#### Smoothed Antichatter
This is a bit of a hybrid system that achieves expected behavior in most cases.

#### Seperated Threshold Mult
Smoothed Antichatter sets a base value for how eager the output cursor is to go to the input position when moving fast.

#### Accel Response Aggressiveness
Explained in the tooltip.

#### Area Scale
Self explanatory. Full area PTK-470 can work with 1. Full area CTL-472 can work with 0.5.

#### X Modifier
This setting is broken on 0.7.0!

Multiplies the X values that the internals of the filter work on and divides outputs by the value. Makes the entire filter screen-space (if done right)! Example: 2560x1440 display (16/9) and 90x60 area (3/2) makes (16/9) / (3/2), which appears to be 1.185185 repeating. This value would be used.

# Custom Reset Absolute Mode
You may need to apply and save multiple times for settings to apply properly.

Multiple binding options are available to move your area. These are pretty much self explanatory and pretty much anything can be done with these abilities.

## Reset Modes

#### Set Tablet Area Center To Position
This effectively puts the cursor to the center of the display setting's area.

#### Drag Tablet Area / Fake Relative Mode
These internally do the same thing, but dragging holds the reset while fake relative mode just triggers it.

#### Set Both Centers To Posiiton
This sets the center of the display area's setting. which changes where setting the tablet area's center to position will put the cursor.

#### Reset To Stock Settings
Self-explanatory. Might need multiple applies/saves to function.
