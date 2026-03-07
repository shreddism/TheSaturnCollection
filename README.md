# The Saturn Collection
 
A set of filters which is planned to grow to include the current general multifilter, as well as more niche optional plugins. For now only the main multifilters exist. This makes the naming convention look a little weird, but it's still correct and allows for additions.

# Multifilter Settings

Settings tooltips will appear on hovering over a setting's textbox. Here's a more proper breakdown of settings that may need them.

If you're reading this without much context, you'll come across the term "velocity racket." I made this up just now to describe an effect of distance-weight antichatter. For an example, use "Kuuube's CHATTER EXTERMINATOR SMOOTH" on 50 strength. It might look like it's just decreasing pen resolution, but it's doing that as an intended side effect. This is velocity racket, where velocity becomes 0 for a report then non-0 for the next, making movement choppier when it doesn't exactly need to.

## Method-exclusive Settings

### Position Interpolation
#### Prediction Ratio
Temporal Resampler puts the latest reverse-smoothed position into a Kalman filter, which spits out a point that it thinks will be next.
Based on Prediction Ratio, the point used for interpolation will go from the latest reverse-smoothed position to the Kalman filter's point.
At 0.0 this step is just foregone entirely. At 0.5 the point lands halfway between the latest reverse-smoothed position and the Kalman filter's point. At 1 only the Kalman filter's point is used.
The point is fed into the 3 points to be used in interpolation.

### Velocity Interpolation
#### Velocity Trajectory Limiter 
The trajectory estimator from Temporal Resampler is used, but on per-report change in position, or velocity.
This ended up having the capability to extrapolate decently well if manual checks were put in place to reduce error. 
The setting goes from 2 to 3 because of it's internal working and I left it to leave an obvious difference.
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
These tablets are known (source: me) to give funny unreliable reports on press/lift. This sticks a control rod in what could be a prediction disaster. In Velocity Interpolation, this also modifies correction to be better.

#### Frequency
Yes, Frequency. This section is carved out to point out to anyone unaware that on Windows, setting Frequency to anything but something that results in an integer-millisecond update interval (so 1000 or 500 in edge cases) will slam the CPU. Things work fine on Linux.
Support for uber-high frequencies is the one thing I'm iffy on because I haven't even tried it yet, which is my fault since I put this together on Linux.

## Other Settings

#### Reverse EMA
Follow [these instructions](https://github.com/X9VoiD/VoiDPlugins/wiki/Reconstructor).

#### Directional Antichatter
Should be explained in the tooltip. Mostly unaffected by aspect ratio compensation.

#### Velocity Outer Range
I'm not entirely sure if this meshes very cleanly with directional antichatter. It at least makes sure not to double-up on keeping velocity in the same place. Should be kept lower than outer radius. Mostly unaffected by aspect ratio compensation.

#### Stock EMA Weight
Self explanatory. Runs at update, but we have a new position every update so it's not an issue unlike Hawku/Devocub. At a super low weight with wire enabled, velocity racket occurs. This may be overhauled in the future in favor of fully using a Radial Follow-like
distance-clamped antichatter instead of a Devocub-like distance-weight antichatter, but its drawbacks have mostly been stomped out, so it became something to be included in the next version.

#### Accel Response Aggressiveness
Reincarnation of "Adaptive Radial Follow." Not flushed out very well, but that's being saved for a potential internal reordering in the next version.

#### Inner Radius
This is importantly unaffected by aspect ratio compensation because I wanted this setting to be an internal check for other behaviors. It probably shouldn't go higher than 10 because of this. This uses a Radial Follow-like calculation.

#### Additional Antichatter and Antichatter Power
This is affected by aspect ratio compensation. Antichatter Power should not go too high because of potential velocity racket. This uses distance-weight calculation, similar to Devocub, which would incur moderate velocity racketing if not for fixes/changes.

#### Directional Separation
Antichatter uses a little trick where the output position is separated from calculation, allowing underaim to be completely taken out. This controls how much it applies to Additional Antichatter.
This should probably always be 1 for multiple reasons, an important one being that it mostly fixes the awful "hook" effect on perpendicular movements.

#### Area Scale
Self explanatory. Full area PTK-470 can work with 1. Full area CTL-472 can work with 0.5.

#### X Modifier
Multiplies X values in different Vector2s to be used in thresholds to mantain visuals on non-forced aspect ratios. This means vertical area holds.
