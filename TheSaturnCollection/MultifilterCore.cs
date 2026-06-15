using System;
using System.Numerics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;
using static Saturn.Utils;

namespace Saturn
{
    public class MultifilterCore 
    {

        public MultifilterCore(Multifilter m) 
        {
            frameShift = m.frameShift;
            reverseSmoothing = m.reverseSmoothing;
            wireMode = m.wireMode;
            rInner = m.rInner;
            stockWeight = m.stockWeight;
            smoothDist = m.smoothDist;
            sepMult = m.sepMult;
            aResponse = m.aResponse;
            dacOuter = m.dacOuter;
            msOverride = m.msOverride;
            areaScale = m.areaScale;
            xMod = m.xMod;
            interp = m.interp;
            tabletToggle = m.tabletToggle;
            Frequency = m.Frequency;
            name = m.name;
        }

        public void Initialize(ITabletReport report) 
        {
            if (interp) {
                if (tabletToggle)
                    IDTablet(name, ref tabletType);

                if (msOverride > 0) {
                    reportMsAvg = msOverride;
                    msAvg = msOverride;
                    correctWeight = startCorrectWeight * expect * (msStandard / msOverride);
                    secAvg = reportMsAvg / 1000f;
                    rpsAvg = 1f / secAvg;
                }

                wireCode = wireMode switch {
                    "Non-Wire - Point" => 1,
                    "Non-Wire - Interp" => 2,
                    "Wire - Point" => 3,
                    "Wire - Interp" => 4,
                    _ => 2
                };
            }
            else {
                correctWeight = startCorrectWeight;
                wireCode = 1;
            }

            pointFlag = ((wireCode & 1) == 1);
            wireFlag = (wireCode > 2);
            wireAdjustFlag = (wireCode == 4);

            if (dacOuter == 0f) 
                adjdWeight = correctWeight * 0.01f;

            ResetValues(new Vector2(report.Position.X * xMod, report.Position.Y));

            adjDacOuter = dacOuter;
            halfSmoothDist = smoothDist * 0.5f;

            emergency = 4;
            eflag = false;
        }
        
        public void HandleConsume(ITabletReport report)
        {
            if (tick < long.MaxValue) tick++;
            if (gtick < long.MaxValue) gtick++;
            if (interp && (tabletType == 1 || tabletType == 2) && (report.Pressure == 0 && pressure[0] > 0) && (pos[0].Y == report.Position.Y)) {   // An extra report with identical position is thrown in. Don't process it.
                InsertAtFirst(pressure, report.Pressure);
                eflag = true;   
                emPos = outputInternal;
                emergency = 4;

                return;
            }

            if (etick < long.MaxValue) etick++;

            consume = true;

            reportTime = (float)reportStopwatch.Restart().TotalMilliseconds;
            consumeDelta = reportTime / 1000f;

            if (reportTime < 25f && reportTime > 0.01f) {
                if (emergency > 0) 
                    emergency--;

                if (interp && msOverride == 0) {
                    reportMsAvg += ((reportTime - reportMsAvg) * 0.1f);
                    rpsAvg += (1f / (consumeDelta) - rpsAvg) * (1f - MathF.Exp(-2f * (consumeDelta)));
                    secAvg = 1f / rpsAvg;
                    msAvg = 1000f * secAvg;
                    correctWeight = startCorrectWeight * expect * (msStandard / reportMsAvg);
                }

                if (kf != null && (tick > 89 || msOverride > 0)) {   
                    if (tabletType == 1 && reportMsAvg >= 3.75f) {
                        kf.SwapID(2, this);
                    }
                    else if (tabletType == 2 && reportMsAvg < 3.75f) {
                        kf.SwapID(1, this);
                    }
                }
                timeFloor = false;
            }
            else {
                emergency = 4;
                if (interp) {
                    eflag = false;
                    ResetValues(new Vector2(report.Position.X * xMod, report.Position.Y));
                }
            }

            StatUpdate(report);

            if (tabletType == 1 && emergency == 0 && tick > 5) {
                altTime = (float)altTimingStopwatch.Restart().TotalMilliseconds;
                tOverride = 1.0f + Math.Max(0.0f, lastTime + (altTime / (msAvg * ((msOverride == 0f || etick < 50) ? 1.001f : 1.0000f))) - ((etick < 50) ? 2.1f : 2.000f));

                if (tOverride == 1f) {
                    ttick = 0;
                }
                else {
                    if (ttick < long.MaxValue) ttick++;

                    if (ttick > 5000) {
                        etick = 48;
                        if (!altTimeWarn) {
                            Log.Write("Multifilter", "Timing warning - went too long without hitting safe time. Hitting soft reset.", LogLevel.Warning, false, false);
                            altTimeWarn = true;
                        }
                    }
                    else {
                        altTimeWarn = false;
                    }
                }

                if (tOverride > 2f) {
                    tOverride -= (MathF.Floor(tOverride) - 1f);
                }

                lastTime = tOverride;
            }

            ConsumeFilterPass(report);
        }

        public void HandleUpdate(ITabletReport report) 
        {
            updateTime = (float)updateStopwatch.Restart().TotalMilliseconds;
            altTime = (float)altTimingStopwatch.Restart().TotalMilliseconds;
            if (emergency > 0) {
                report.Pressure = pressure[0];
                lastTime = 0f;
                etick = 0;

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

            t = Math.Clamp(t, 0f, 3f);


            if (tabletType == 1 && emergency == 0 && tick > 5) {
                tOverride += (altTime) / (msAvg * ((msOverride == 0f || etick < 50) ? 1.001f : 1.0000f));
                t = tOverride;
                lastTime = t;
            }

            

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

            consume = false;
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

            if (interp) {
                if (dir[0] == pos[0]) {
                    emergency = 4;
                    dir[0] = Vector2.Zero;
                    eflag = false;
                }
                else if (((tabletType == 1 || tabletType == 2 || tabletType == 5) && (pressure[0] > 0 && pressure[1] == 0)) || (tabletType == 5 && (pressure[0] == 0 && pressure[1] > 0))) {
                    if (emergency == 0) 
                        eflag = true;

                    emPos = outputInternal;
                    emergency = 4;
                }
            }
        }

        

        void ConsumeFilterPass(ITabletReport report) 
        {

            if (interp) {
                PredictPass();    
                tOffset += secAvg - consumeDelta;
                tOffset *= MathF.Exp(-5f * consumeDelta);
                tOffset = Math.Clamp(tOffset, -secAvg, secAvg);
                latestReport = runningStopwatch.Elapsed + TimeSpan.FromSeconds(tOffset); 

            }
            else {
                InsertAtFirst(svpos, smpos[0]);
                InsertAtFirst(prpos, smpos[0]);
                InsertAtFirst(svdir, dir[0]);
                InsertAtFirst(prdir, dir[0]);
            }
            
            DAC();

            if (pointFlag) {
                startOutput = stpos[0];
                FilterPass();
                InsertAtFirst(fipos, adaptOutput);
            }
        }
        
        void PredictPass() 
        {
            Vector2 predict = smpos[0];

            if (frameShift > 0f && kf != null) {
                nonconf = ((tabletType == 1 || tabletType == 2 || tabletType == 5) && ((emergency > 0) || (pressure[0] == 0 && Vector2.Distance(dir[0], dir[1] + ddir[1]) > (vel[0] / 8))));
                
                predict = kf.Update(smpos[0], this);

                InsertAtFirst(rk, predict);

                if (!nonconf) {
                    float fac = Smoothstep(Vector2.Distance(dir[0], dir[2]), 5.0f, 0.0f) * Smoothstep(vel[0], 0.0f, 5.0f);
                    float faca = Smoothstep(Vector2.Distance(dir[0], dir[2]), 5.0f, 0.0f) * Smoothstep(vel[0], 0.0f, 5.0f);
                    predict = Vector2.Lerp(predict, Vector2.Lerp(smpos[0], crpos[0], 0.45f) + dir[0], fac);

                    if ((tabletType == 1 || tabletType == 2 || tabletType == 4) && (Vector2.Distance(dir[0], dir[2]) > Vector2.Distance(dir[0], dir[1])) && (vel[0] > 20.0f)) {
                        float frame1 = tabletType switch {
                            1 => 3.0f + Smoothstep(vel[0], 500.0f, 150.0f) * (0.1f * Smoothstep(jerk[0], 10f, 50f) - 1f * Smoothstep(jerk[0], -10f, -50f)),
                            2 or 4 => 3.0f + Smoothstep(vel[0], 500.0f, 150.0f) * (0.1f * Smoothstep(jerk[0], 20f, 50f) - 0.3f * Smoothstep(jerk[0], -20f, -50f)),
                            _ => 3.0f,
                        };
                        
                        Vector2 x1 = Trajectory(dir[0], dir[1], dir[2], frame1);

                        float f1 = Smoothstep(vel[0] + Vector2.Distance(dir[0], dir[2]), 0.0f, 100.0f) * Smoothstep(((dir[0] - dir[1]) + (dir[1] - dir[2])).Length(), 10.0f, 50.0f);

                        predict = Vector2.Lerp(predict, Vector2.Lerp(smpos[0], crpos[0], 0.585f - 0.05f * Smoothstep((Math.Abs(accel[0]) + Math.Abs(jerk[0])) * spro(vel[0] / 100), 0.0f, 250.0f)) + x1, Math.Max(0.0f, (0.725f + 0.025f * Smoothstep(vel[0] + Math.Abs(accel[0]) + Math.Abs(jerk[0]), 50.0f, 150.0f)) * f1));

                        if (DotNorm(dir[0], dir[5], 0.0f) > 0.9f) {
                            Vector2 x2 = Trajectory((dir[0] + dir[1]) * 0.5f, (dir[2] + dir[3]) * 0.5f, (dir[4] + dir[5]) * 0.5f, 2.75f); 
                            float f2 = Smoothstep(vel[0] + Math.Abs(accel[0]), 0.0f, 100.0f) * Smoothstep(vel[0] + Vector2.Distance(dir[0], dir[1]), 10.0f, 30.0f) * Smoothstep(Vector2.Distance(dir[0], dir[2]), 3.0f, 20.0f) * Smoothstep(Math.Abs(accel[0]) + Math.Abs(jerk[0]), 25.0f, 10.0f);

                            predict = Vector2.Lerp(predict, Vector2.Lerp(smpos[0], rk[1], 0.35f * Smoothstep(Math.Abs(accel[0]), 25.0f, 5.0f)) + x2, Math.Max(0.0f, 0.5f * (f2 - f1)));

                            if (tabletType == 1) {
                                Vector2 x3 = dir[0] + Trajectory((ddir[0] + ddir[1]) * 0.5f, (ddir[2] + ddir[3]) * 0.5f, (ddir[4] + ddir[5]) * 0.5f, 2.75f); 
                                float f3 = Smoothstep(Vector2.Distance(ddir[0], ddir[5]), 10.0f, 25.0f) * Smoothstep(vel[0] * spro(Math.Abs(jerk[0]) / 10), 100.0f, 425.0f);
                                predict = Vector2.Lerp(predict, Vector2.Lerp(smpos[0], crpos[0], 0.20f) + x3, Math.Max(0.0f, 0.5f * f3 - 0.75f * f1 - 0.75f * f2));
                            }
                        }                               
                    }
                }

                if (pointFlag && emergency > 0) {
                    predict = smpos[0];
                }

                InsertAtFirst(crpos, predict);
                InsertAtFirst(crdir, crpos[0] - crpos[1]);

                if (!nonconf && tabletType == 1 || tabletType == 2 || tabletType == 4) {
                    float kvv = 0.25f * Smoothstep(vel[0], 10 * areaScale, 30 * areaScale) * Smoothstep(Math.Abs(accel[0]) + Math.Abs(jerk[0]), 20.0f * areaScale, 5.0f * areaScale) * Smoothstep(Vector2.Distance(dir[0], dir[5]), 5.0f * areaScale, 10.0f * areaScale) * Smoothstep((ddir[0] + ddir[1] + ddir[2] + ddir[3] + ddir[4] + ddir[5]).Length(), 30.0f * areaScale, 25f * areaScale);
                    if (kvv > kvw)  {
                        kvw = 0.25f * kvv + 0.75f * kvw;
                    }
                    else {
                        kvw = kvv;
                    }
                    Vector2 kv = kvf!.Update(dir[0], this);
                    InsertAtFirst(c1d, Vector2.Lerp(crdir[0], kv, kvw));
                    InsertAtFirst(c1p, crpos[1] + c1d[0] * (1.0f + 0.01f * Smoothstep(jerk[0], 10f, 50f) - 0.01f * Smoothstep(jerk[0], -10f, -50f)));
                }
                else {
                    InsertAtFirst(c1p, crpos[0]);
                    InsertAtFirst(c1d, crdir[0]);
                }

                predict = c1p[0];

                predict += (smpos[0] - predict) * (1f - frameShift);

                InsertAtFirst(prpos, predict);
                InsertAtFirst(prdir, prpos[0] - prpos[1]);

                InsertAtFirst(svpos, prpos[0]);
                InsertAtFirst(svdir, prdir[0]);
            }
            else {
                InsertAtFirst(svpos, smpos[0]);
                InsertAtFirst(prpos, smpos[0]);
                InsertAtFirst(svdir, dir[0]);
                InsertAtFirst(prdir, dir[0]);
            }

            if (tabletType == 1 && Vector2.Distance(prpos[0], pos[0]) > 500f + vel[0]) {
                emergency = 4;
            }
        }


        void DAC() 
        {
            if (dacOuter > 0f) {
                float vscale = Smoothstep(vel[0], 5, 10 + adjDacOuter);
                float scale = MathF.Pow(Smoothstep(Math.Max(pointaccel[0], Vector2.Distance(stdir[0], dir[0])), -0.01f, (vscale * adjDacOuter)), 3);
                adjdWeight = correctWeight * Smoothstep(vel[0], 5, 10) * Math.Clamp(scale + 1 - vscale, 0.25f, 1f);
                Vector2 stabilized = Vector2.Lerp(stdir[0], svdir[0], scale);  
                InsertAtFirst(stdir, stabilized);
                Vector2 stpoint = stpos[0] + stdir[0];
                InsertAtFirst(stpos, stpoint);
                stpos[0] = Vector2.Lerp(stpos[0], svpos[0], adjdWeight);
            }
            else {
                InsertAtFirst(stpos, svpos[0]);
                InsertAtFirst(stdir, svdir[0]);
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
            float weight = 1;

            if (aResponse > 0f) {
                float aDist = Vector2.Distance(smoothOutput, adaptOutput);
                aMod = (1 + MathF.Log10(Math.Max(aResponse, 1f))) * MathF.Pow(Smoothstep(aDist, 3500 * aResponse * areaScale, (500 * MathF.Sqrt(aResponse * areaScale)) - 1.0f) * Smoothstep(accel[0] + Math.Max(0, jerk[0]) / spro(vel[0] / 250), 10 * areaScale, 50 * areaScale), 2.5f + aResponse * areaScale) * DotNorm(ddir[0], dir[0], 0);
                weight = Math.Clamp(1 - aMod, 0, 1);
                weight *= 1.0f - 0.75f * (Smoothstep(aDist, 1000 * areaScale, 5000 * areaScale) * Smoothstep(vel[0] + accel[0], 250 * areaScale, 500 * areaScale));
            }

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
            tick = 0;
            kf = new KalmanVector2(p, this);
            int hold = tabletType;
            tabletType = 67;
            kvf = new KalmanVector2(p, this);
            tabletType = hold;
            pos = Enumerable.Repeat(p, HMAX).ToArray();
            stpos = Enumerable.Repeat(p, HMAX).ToArray();
            smpos = Enumerable.Repeat(p, HMAX).ToArray();
            prpos = Enumerable.Repeat(p, HMAX).ToArray();
            crpos = Enumerable.Repeat(p, HMAX).ToArray();
            svpos = Enumerable.Repeat(p, HMAX).ToArray();
            svdir = Enumerable.Repeat(p, HMAX).ToArray();
            fipos = Enumerable.Repeat(p, HMAX).ToArray();
            c1p = Enumerable.Repeat(p, HMAX).ToArray();
            rk = Enumerable.Repeat(p, HMAX).ToArray();
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

        public void IDTablet(string name, ref int tabletType) {
            Identify(name, ref tabletType);
            Log.Write("Multifilter", "Tablet: " + name);
            switch (tabletType) {
                case 1:
                    Log.Write("Multifilter", "Prediction is enhanced heavily.");
                    Log.Write("Multifilter", "Press/lift bugging is mitigated.");
                    if (msOverride == 0f) {
                        Log.Write("Multifilter", "Confident timing system is in use (No timing override).");
                        Log.Write("Multifilter", "Consider using 3.3025 for the Expected Milliseconds Per Report setting.");
                    }
                    else {
                        if (msOverride == 3.3025f) {
                            Log.Write("Multifilter", "Confident timing system is in use.");
                        }
                        else {
                            Log.Write("Multifilter", "Confident timing system may or may not be in use. You're on your own here.");
                        }
                    }
                    rpsAvg = 302.8f;
                    secAvg = 0.0033025f;
                    msAvg = 3.3025f;
                    reportMsAvg = 3.3025f;
                break;
                case 2:
                    Log.Write("Multifilter", "Prediction is enhanced (hopefully).");
                    Log.Write("Multifilter", "Press/lift bugging is mitigated (hopefully).");
                break;
                case 3:
                    Log.Write("Multifilter", "Prediction is enhanced (hopefully).");
                break;
                case 4:
                    Log.Write("Multifilter", "Prediction is enhanced (hopefully).");
                break;
                case 5:
                    Log.Write("Multifilter", "Prediction is enhanced (hopefully).");
                    Log.Write("Multifilter", "Press/lift bugging is mitigated (hopefully).");
                break;
                default:
                    Log.Write("Multifilter", "No changes to be made.");
                break;
            }
        }
        
        public bool consume;
        public string name;
        public int tabletType;
        public Vector2[] pos = new Vector2[HMAX];
        public Vector2[] dir = new Vector2[HMAX];
        public Vector2[] ddir = new Vector2[HMAX];
        public Vector2[] fipos = new Vector2[HMAX];
        public Vector2[] prpos = new Vector2[HMAX];
        public Vector2[] crpos = new Vector2[HMAX];
        public Vector2[] crdir = new Vector2[HMAX];
        public Vector2[] c1d = new Vector2[HMAX];
        public Vector2[] c1p = new Vector2[HMAX];
        public Vector2[] rk = new Vector2[HMAX];
        public Vector2[] prdir = new Vector2[HMAX];
        public Vector2[] stpos = new Vector2[HMAX];
        public Vector2[] stdir = new Vector2[HMAX];
        public Vector2[] smpos = new Vector2[HMAX];
        public Vector2[] svpos = new Vector2[HMAX];
        public Vector2[] svdir = new Vector2[HMAX];
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
        public bool altTimeWarn;
        public int emergency;
        public float reportMsAvg;
        public float sepScale;
        public float kvw;
        public float lastTime;
        public float tOverride;
        public bool timeFloor;
        public float altTime;
        public const float startCorrectWeight = 0.1f;    
        public const float msStandard = 3.302466f;
        public float expect => 1000 / Frequency;
        public long tick = 0;
        public long etick = 0;
        public long ttick = 0;
        public long gtick = 0;
        public HPETDeltaStopwatch reportStopwatch = new HPETDeltaStopwatch();
        public HPETDeltaStopwatch updateStopwatch = new HPETDeltaStopwatch();
        public HPETDeltaStopwatch altTimingStopwatch = new HPETDeltaStopwatch();
        public KalmanVector2? kf;
        public KalmanVector2? kvf;
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
        public float frameShift, reverseSmoothing, dacOuter;
        public string wireMode;
        public float rInner, stockWeight, smoothDist, sepMult, aResponse, msOverride, areaScale, xMod;
        public bool tabletToggle;
        public Vector2 clampHold, clampOutput;
        public float halfSmoothDist;
        public Vector2 smoothOutput;
        public Vector2 adaptOutput;
        public bool interp;
        public int wireCode;
        public float adjDacOuter;
        public float updateTime;
        public bool wireFlag, pointFlag, wireAdjustFlag, eflag, nonconf;
        public float Frequency;
    }
}