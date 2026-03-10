# The Saturn Collection [![Total Download Count](https://img.shields.io/github/downloads/shreddism/TheSaturnCollection/total.svg)](https://github.com/shreddism/TheSaturnCollection/releases)
 
A set of filters which is planned to grow to include the current multifilter for all different types of users, as well as more niche optional plugins, like a velocity curve for artists or other general users who can contend with tablet drift for their use case. For now only the main multifilters exist. This makes the naming convention look a little weird, but it's still correct and allows for additions.

Formatting may be non-final.

### Important

In version 0.6.5, there are various issues with uncommon settings. To be safe, set Directional Antichatter 'Inner Radius' to 0, set Directional Antichatter 'Outer Radius' to 0.01, and set Velocity 'Outer Range' to 0. If using Velocity Interpolation, keep Frequency at 1000hz.

If you are reading this, then issues with these settings are fixed and any settings will work, but the update is awaiting being merged. If you really can't wait:

Uninstall your current version first. Then, do this in Plugin Manager > File > Use alternate source...

![](/TheSaturnCollection/image/tscpatch1.png)

Hit apply, and you'll find the version is now 0.7.0, the version with fixes.

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
For interpolation, time is used anyway, so it's fine. For any sort of iteration of smoothing, without warning the smoothing, you're usually going to be covering the same exact distance in 0.1 milliseconds as you do in 1. Velocity racket may occur.
There are watches and modifiers in place to [mostly fix](https://github.com/shreddism/TheSaturnCollection/blob/d4ea5c068d202ce4548b595f9c9a2a8a12c0224a/TheSaturnCollection/Mposition.cs#L428) this.

#### Expected Milliseconds Per Report Override
Interpolation uses timing averages of inconsistent integer millisecond report times to generally know what to do. I thought it would be reassuring to add a hard override for those who know their tablet's average. Doesn't take effect at 0.

#### "Wacom PTK-x70 Series Toggle"
This is said in the tooltip, but this may apply to people with PTH-x60 tablets as well, it's just not been tested/confirmed yet.
These tablets are known (source: me) to give funny unreliable position reports on press/lift (which is a PRESSURE thing, not a TILT thing, to prevent misreads, and it's a HARDWARE feature that cannot be disabled in OTD) that mess up all prediction.
This sticks a control rod in what could be a prediction disaster. In Velocity Interpolation, this also modifies correction to be better IF your tablet is trustworthy.

#### Frequency
Yes, Frequency. This section is carved out to point out to anyone unaware that on Windows, setting Frequency to anything but something that results in an integer-millisecond update interval (so 1000 or 500 in edge cases) will slam the CPU.
If your CPU can handle it, this will be fine, but system timing when it comes to the "Wire" setting may be inaccurate (untested). All frequencies work fine on Linux, so you can just set it to 1x or 2x your display refresh rate without worry.
Support for uber-high frequencies is the one thing I'm iffy on because I haven't even tried it yet, which is my fault since I put this together on Linux.

## Other Settings

#### Reverse EMA
Follow [these instructions](https://github.com/X9VoiD/VoiDPlugins/wiki/Reconstructor#optimal-configuration).

#### Directional Antichatter
Should be explained in the tooltip. Mostly unaffected by aspect ratio compensation.

#### Stock EMA Weight
Self explanatory. Runs at update, but we have a new position every update so it's not an issue unlike Hawku/Devocub. At a super low weight with wire enabled, velocity racket occurs. This may be overhauled in the future in favor of fully using a Radial Follow-like
distance-clamped antichatter instead of a Devocub-like distance-weight antichatter, but its drawbacks have mostly been stomped out, so it became something to be included in the next version.

#### Accel Response Aggressiveness
Explained in the tooltip. Not flushed out very well, but that's being saved for a potential internal reordering in the next version.

#### Inner Radius
This is affected by aspect ratio compensation, but not in Velocity Interpolation, because I wanted this setting to be an internal check for other behaviors, and I want to gather opinions on whether or not that's a good idea. This uses a Radial Follow-like calculation.

#### Additional Antichatter and Antichatter Power
This is affected by aspect ratio compensation. Antichatter Power should not go too high because of potential velocity racket. This uses distance-weight calculation, similar to Devocub, which would incur moderate velocity racketing if not for fixes/changes.

#### Directional Separation
Antichatter uses a little trick where the output position is separated from calculation, allowing underaim to be completely taken out. This controls how much it applies to Additional Antichatter.
This should probably always be 1 for multiple reasons, an important one being that it mostly fixes the awful "hook" effect on perpendicular movements.

#### Area Scale
Self explanatory. Full area PTK-470 can work with 1. Full area CTL-472 can work with 0.5.

#### X Modifier
Multiplies X values in different Vector2s to be used in thresholds to mantain visuals on non-forced aspect ratios. This means vertical area holds.
