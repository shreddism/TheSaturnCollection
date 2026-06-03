using System;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;
using static Saturn.Utils;

namespace Saturn
{
    public class InterpFilter 
    {
        public InterpFilter(MultifilterI m) 
        {
            frameShift = m.frameShift;
            reverseSmoothing = m.reverseSmoothing;
            dacInner = m.dacInner;
            dacOuter = m.dacOuter;
            wireMode = m.wireMode;
            rInner = m.rInner;
            stockWeight = m.stockWeight;
            smoothDist = m.smoothDist;
            sepMult = m.sepMult;
            aResponse = m.aResponse;
            msOverride = m.msOverride;
            areaScale = m.areaScale;
            xMod = m.xMod;
            tabletToggle = m.tabletToggle;
            Frequency = m.Frequency;
            name = m.name;
        }

        public void Initialize(ITabletReport report) 
        {
            if (tabletToggle)
                Identify(this);

            if ((tabletType == 1 || tabletType == 2) && tabletToggle) 
                pflag = true;
            
            halfSmoothDist = smoothDist * 0.5f;

            if (msOverride > 0) {
                reportMsAvg = msOverride;
                msAvg = msOverride;
                correctWeight = startCorrectWeight * expect * (msStandard / msOverride);
                secAvg = reportMsAvg / 1000f;
                rpsAvg = 1f / secAvg;

                if (dacInner + dacOuter == 0f) 
                    adjdWeight = correctWeight * 0.01f;
            }

            adjDacOuter = Math.Max(dacOuter, dacInner + 0.01f);

            wireCode = wireMode switch {
                "Non-Wire - Point" => 1,
                "Non-Wire - Interp" => 2,
                "Wire - Point" => 3,
                "Wire - Interp" => 4,
                _ => 1
            };

            pointFlag = ((wireCode & 1) == 1);
            wireFlag = (wireCode > 2);
            wireAdjustFlag = (wireCode == 4);

            ResetValues(new Vector2(report.Position.X * xMod, report.Position.Y));

            emergency = 4;
            eflag = false;
        }
        
        public int HandleConsume(ITabletReport report)
        {
            if (pflag && (report.Pressure == 0 && pressure[0] > 0) && (pos[0].Y == report.Position.Y)) {   // An extra report with identical position is thrown in. Don't process it.
                InsertAtFirst(pressure, report.Pressure);
                eflag = true;   
                emPos = outputInternal;
                emergency = 4;

                if (wireFlag) 
                    return 1;

                return 2;
            }

            reportTime = (float)reportStopwatch.Restart().TotalMilliseconds;
            consumeDelta = reportTime / 1000f;

            if (reportTime < 25f && reportTime > 0.01f) {
                if (msOverride == 0) {
                    reportMsAvg += ((reportTime - reportMsAvg) * 0.1f);
                    rpsAvg += (1f / (consumeDelta) - rpsAvg) * (1f - MathF.Exp(-2f * (consumeDelta)));
                    secAvg = 1f / rpsAvg;
                    msAvg = 1000f * secAvg;
                    correctWeight = startCorrectWeight * expect * (msStandard / reportMsAvg);
                }

                if (emergency > 0) 
                    emergency--;
            }
            else {
                emergency = 4;
                eflag = false;
                ResetValues(new Vector2(report.Position.X * xMod, report.Position.Y));
            }

            StatUpdate(report);

            ConsumeFilterPass(report);
            
            if (wireFlag)
                return 1;

            return 0;
        }

        public void HandleUpdate(ITabletReport report) 
        {
            updateTime = (float)updateStopwatch.Restart().TotalMilliseconds;
            if (emergency > 0) {
                report.Pressure = pressure[0];

                if (eflag) {
                    if (!pointFlag) {
                        startOutput = pos[0];
                        FilterPass();
                    }
                
                    float eTime = ((float)reportStopwatch.Elapsed.TotalSeconds * Frequency / reportMsAvg) * (expect);
                    float scale = Math.Min((((float)(4 -  emergency) + Math.Min(eTime, 1.0f)) * 0.25f), 1.0f);
                    outputInternal = Vector2.Lerp(emPos, adaptOutput, scale); 
                    report.Position = new Vector2(outputInternal.X / xMod, outputInternal.Y);
                    dirOfOutput = (report.Position - lastOutputPos) / updateTime;
                    lastOutputPos = report.Position;
                }
                else { 
                    ERefresh();
                    emPos = pos[0];
                    report.Position = new Vector2(adaptOutput.X / xMod, adaptOutput.Y);
                    lastOutputPos = report.Position;
                }

                return;
            } 

            float t = 1 + (float)(runningStopwatch.Elapsed - latestReport).TotalSeconds * rpsAvg;
            t = Math.Clamp(t, 0, 3);

            if (pointFlag) {
                outputInternal = RTrajectory(t, fipos[2], fipos[1], fipos[0]);
            }
            else {
                startOutput = RTrajectory(t, stpos[2], stpos[1], stpos[0]);
                FilterPass();
                outputInternal = adaptOutput;
            }

            emPos = outputInternal;
            report.Position = new Vector2(outputInternal.X / xMod, outputInternal.Y);
            dirOfOutput = (report.Position - lastOutputPos) / updateTime;
            lastOutputPos = report.Position;
            report.Pressure = pressure[0];   

            if (!vec2IsFinite(report.Position + startOutput + clampOutput + smoothOutput + adaptOutput + outputInternal)) {
                ERefresh();
                emPos = pos[0];
                eflag = false;
                emergency = 4;
                ResetValues(pos[0]);
                report.Position = new Vector2(outputInternal.X / xMod, outputInternal.Y);
            }       
        }
        
        public void StatUpdate(ITabletReport report) 
        {
            InsertAtFirst(pos, report.Position);
            pos[0].X *= xMod;
            Vector2 smoothed = pos[0];

            if (reverseSmoothing < 1f && reverseSmoothing > 0f)
                smoothed = pos[1] + (pos[0] - pos[1]) / reverseSmoothing;

            InsertAtFirst(smpos, smoothed);
            InsertAtFirst(dir, smpos[0] - smpos[1]);
            InsertAtFirst(vel, dir[0].Length());
            InsertAtFirst(ddir, dir[0] - dir[1]);
            InsertAtFirst(accel, vel[0] - vel[1]);
            InsertAtFirst(jerk, accel[0] - accel[1]);
            InsertAtFirst(pointaccel, ddir[0].Length());
            InsertAtFirst(pressure, report.Pressure);

            if (dir[0] == pos[0]) {
                emergency = 4;
                dir[0] = Vector2.Zero;
                eflag = false;
            }
            else if (pflag && (pressure[0] > 0 && pressure[1] == 0)) {
                if (emergency == 0) 
                    eflag = true;

                emPos = outputInternal;
                emergency = 4;
            }
        }

        void ConsumeFilterPass(ITabletReport report) 
        {
            Vector2 predict = PredictPass();         
            InsertAtFirst(prpos, predict);
            InsertAtFirst(prdir, prpos[0] - prpos[1]);   
            
            tOffset += secAvg - consumeDelta;
            tOffset *= MathF.Exp(-5f * consumeDelta);
            tOffset = Math.Clamp(tOffset, -secAvg, secAvg);
            latestReport = runningStopwatch.Elapsed + TimeSpan.FromSeconds(tOffset);
            
            DAC();

            if (pointFlag) {
                startOutput = stpos[0];
                FilterPass();
                InsertAtFirst(fipos, adaptOutput);
            }
        }

        Vector2 PredictPass() 
        {
            Vector2 predict = smpos[0];

            if (frameShift > 0f) {
                nonconf = (pflag && ((emergency > 0) || (pressure[0] == 0 && Vector2.Distance(dir[0], dir[1] + ddir[1]) > (vel[0] / 5))));

                if (kf != null) {
                    predict = kf.Update(smpos[0], this);

                    if (tabletType == 1) {
                        float fact = 0f;
                        if (!nonconf) {
                            float fac = Smoothstep(Vector2.Distance(dir[0], dir[2]), 2.5f, 7.5f);
                            predict = Vector2.Lerp(predict, smpos[0] + dir[0] + ddir[0], fac);
                            
                            fact = fac * Smoothstep(Math.Abs(jerk[0]) + (Math.Abs(accel[0]) / 2.0f), 5.0f, 20.0f);
                            predict += fact * (ddir[0] - ddir[1]);
                        }

                        predict += (smpos[0] - predict) * (1f - frameShift);
                        
                        if (!nonconf && Vector2.Distance(dir[0], dir[2]) > Vector2.Distance(dir[0], dir[1]) && vel[0] > 20.0f) {
                            Vector2 x1 = Trajectory(dir[0], dir[1], dir[2], 2.0f + frameShift);
                            float f1 = Smoothstep(vel[0] + (accel[0] * spro((vel[0] + Math.Abs(accel[0])) / 25.0f)), 20.0f, 40.0f) * Smoothstep(Vector2.Distance(dir[0], dir[2]), 10.0f, 30.0f);

                            predict = Vector2.Lerp(predict, smpos[0] + x1, Math.Max(0.0f, 0.75f * f1));

                            Vector2 x2 = Trajectory((dir[0] + dir[1]) * 0.5f, (dir[2] + dir[3]) * 0.5f, (dir[4] + dir[5]) * 0.5f, 2.25f + 0.5f * frameShift);   // https://www.desmos.com/calculator/oecxkq2eue
                            float f2 = Smoothstep(vel[0] + accel[0], 10.0f, 40.0f) * Smoothstep(Vector2.Distance(dir[0], dir[2]), 3.0f, 20.0f) * Smoothstep(Math.Abs(accel[0]) + Math.Abs(jerk[0]), 50.0f, 10.0f);

                            if (DotNorm(dir[0], dir[5], 0.0f) > 0.9f && DotNorm(ddir[0], ddir[5], 0.0f) > 0.9f) {
                                predict = Vector2.Lerp(predict, smpos[0] + x2, Math.Max(0.0f, 0.9f * (f2 - f1)));
                            }                                
                        }
                    }
                }
            }

            return predict;
        }

        void DAC() 
        {
            if (dacInner + dacOuter > 0f) {
                float vscale = Smoothstep(vel[0], 5, 10 + adjDacOuter);
                float scale = MathF.Pow(Smoothstep(Math.Max(pointaccel[0], Vector2.Distance(stdir[0], prdir[0])), Math.Max(0, vscale * dacInner) - 0.01f, (vscale * adjDacOuter)), 3);
                adjdWeight = correctWeight * Math.Clamp(scale + 1 - vscale, 0.25f, 1f);
                Vector2 stabilized = Vector2.Lerp(stdir[0], prdir[0], scale);  
                InsertAtFirst(stdir, stabilized);
                Vector2 stpoint = stpos[0] + stdir[0];
                InsertAtFirst(stpos, stpoint);
                stpos[0] = Vector2.Lerp(stpos[0], prpos[0], adjdWeight);
            }
            else {
                InsertAtFirst(stdir, dir[0]);
                InsertAtFirst(stpos, pos[0]);
                stpos[0] = Vector2.Lerp(stpos[0], prpos[0], adjdWeight);
            }
        }

        void RF() 
        {
            Vector2 dist = startOutput - clampHold;
            float distLength = dist.Length();
            Vector2 ringDir = Math.Max(0, distLength - (rInner)) * Default(Vector2.Normalize(dist), Vector2.Zero);
            float ringDirLength = ringDir.Length();
            clampHold += ringDir;
            clampOutput += ringDir;

            if (ringDirLength > 0 || distLength > rInner || accel[0] < -10 * areaScale || vel[0] > 10 * rInner) {
                float xwa = XWA(expect, updateTime, wireAdjustFlag, reportMsAvg, expect, pointFlag);
                clampOutput = Vector2.Lerp(clampOutput, startOutput, UAdjust(Smoothstep(ringDirLength, -0.01f, rInner), xwa));
                clampOutput = Vector2.Lerp(clampOutput, startOutput, UAdjust(Smoothstep(accel[0], -10 * areaScale, -150 * areaScale), xwa));
            }
        }

        void AEMA() 
        {
            Vector2 dist = clampOutput - smoothHold;
            float distLength = dist.Length();
            float mLength = DSFunction(distLength, smoothDist, halfSmoothDist);
            float wcon = WireWeightAdjust(stockWeight * Default(mLength / distLength, 0), expect, updateTime, wireAdjustFlag);
            smoothHold += wcon * dist;
            smoothOutput = smoothHold;

            if (sepMult > 0 && mLength > 0) {
                if (!(wireFlag) || updateTime / expect > 0.99f) 
                    sepScale = Smoothstep(distLength, -0.01f, smoothDist * sepMult);
                
                smoothOutput = Vector2.Lerp(smoothHold, Vector2.Lerp(smoothHold, clampOutput, stockWeight), sepScale);
            }

            float aMod = 0;

            if (aResponse > 0f) {
                float aDist = Vector2.Distance(smoothOutput, adaptOutput);
                aMod = (1 + MathF.Log10(Math.Max(aResponse, 1f))) * MathF.Pow(Smoothstep(aDist, 2500 * aResponse, (500 * aResponse) - 1.0f) * Smoothstep(accel[0] + Math.Max(0, jerk[0]), 10 * areaScale, 25 * areaScale), 3.0f) * DotNorm(ddir[0], dir[0], 0); // :)
            }

            float weight = Math.Clamp(1 - aMod, 0, 1);
            adaptOutput = Vector2.Lerp(adaptOutput, smoothOutput, WireWeightAdjust(weight, expect, updateTime, wireAdjustFlag));
        }

        void FilterPass()
        {
            if (rInner > 0f)
                RF(); 
            else 
                clampOutput = startOutput;

            AEMA();
        }

        void ResetValues(Vector2 p) 
        {
            kf = new KalmanVector2(p, this);
            pos = Enumerable.Repeat(p, pos.Length).ToArray();
            stpos = Enumerable.Repeat(p, stpos.Length).ToArray();
            smpos = Enumerable.Repeat(p, smpos.Length).ToArray();
            prpos = Enumerable.Repeat(p, prpos.Length).ToArray();
            fipos = Enumerable.Repeat(p, fipos.Length).ToArray();
            latestReport = runningStopwatch.Elapsed;
            tOffset = 0;
        }

        void ERefresh() 
        {
            startOutput = pos[0];
            clampHold = pos[0];
            clampOutput = pos[0];
            smoothHold = pos[0];
            smoothOutput = pos[0];
            adaptOutput = pos[0];
            outputInternal = pos[0];
        }

        Vector2 RTrajectory(float t, Vector2 v3, Vector2 v2, Vector2 v1)
        {
            var mid = 0.5f * (v1 + v3);
            var accel = 2f * (mid - v2);
            var vel = 2f * v2 - v3 - mid;

            // if there is acceleration, then start spacing points evenly using integrals
            if (Vector2.Dot(accel, accel) > 0.001f)
            {
                int floor = (int)Math.Floor(t);
                var _vel = vel + accel * floor;

                // if any of the inputs have changed, recalculate arcArr
                if ((_floor != floor) || (_v1 != v1) || (_v2 != v2) || (_v3 != v3))
                {
                    _v1 = v1;
                    _v2 = v2;
                    _v3 = v3;
                    _floor = floor;
                    arcTar = 0;

                    for (int _t = 0; _t < steps; _t++)
                    {
                        arcArr[_t] = arcTar;
                        arcTar += (_vel + _t * dt * accel).Length();
                    }
                }

                float _arcTar = arcTar * (t - floor);

                for (int _t = 0; _t < steps; _t++)
                {
                    if (arcArr[_t] < _arcTar) continue;
                    t = _t * dt + floor;
                    break;
                }
            }

            return v3 + t * vel + 0.5f * t * t * accel;
        }
        
        public string name;
        public int tabletType;
        public Vector2[] pos = new Vector2[HMAX];
        public Vector2[] dir = new Vector2[HMAX];
        public Vector2[] ddir = new Vector2[HMAX];
        public Vector2[] fipos = new Vector2[HMAX];
        public Vector2[] prpos = new Vector2[HMAX];
        public Vector2[] prdir = new Vector2[HMAX];
        public Vector2[] stpos = new Vector2[HMAX];
        public Vector2[] stdir = new Vector2[HMAX];
        public Vector2[] smpos = new Vector2[HMAX];
        public Vector2 smoothHold, emPos;
        public float[] vel = new float[HMAX];
        public float[] accel = new float[HMAX];
        public float[] jerk = new float[HMAX];
        public float[] pointaccel = new float[HMAX];
        public uint[] pressure = new uint[HMAX];
        public Vector2 startOutput, outputInternal;
        public Vector2 lastOutputPos, dirOfOutput;
        public float reportTime;
        public float adjdWeight;
        public float correctWeight;
        public bool init = false;
        public int emergency;
        public float reportMsAvg;
        public float sepScale;
        public const float startCorrectWeight = 0.01f;    
        public const float msStandard = 3.302466f;
        public float expect => 1000 / Frequency;
        public HPETDeltaStopwatch reportStopwatch = new HPETDeltaStopwatch();
        public HPETDeltaStopwatch updateStopwatch = new HPETDeltaStopwatch();
        public KalmanVector2? kf;
        public TimeSpan latestReport = TimeSpan.Zero;
        public float rpsAvg = 200f, tOffset;
        public float msAvg = 5;
        public float secAvg = 0.005f;
        public float consumeDelta;
        public HPETDeltaStopwatch runningStopwatch = new HPETDeltaStopwatch(true);
        private static readonly int steps = 256;
        private static readonly float dt = 1f / steps;
        private float[] arcArr = new float[steps];
        private float arcTar = 0;
        private Vector2 _v1, _v2, _v3;
        private int _floor;
        public float frameShift, reverseSmoothing, dacInner, dacOuter;
        public string wireMode;
        public float rInner, stockWeight, smoothDist, sepMult, aResponse, msOverride, areaScale, xMod;
        public bool tabletToggle;
        public Vector2 clampHold, clampOutput;
        public float halfSmoothDist;
        public Vector2 smoothOutput;
        public Vector2 adaptOutput;
        public int wireCode;
        public float adjDacOuter;
        public float updateTime;
        public bool wireFlag, pointFlag, wireAdjustFlag, eflag, pflag, nonconf;
        public float Frequency;
    }
}