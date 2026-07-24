using System;
using System.Runtime.Intrinsics;
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

            savedFilter = m;
            config = m.config;
            ExGate = m.ExGate;
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
            tabletToggle = !(ExGate && config!.Enabled && config!.DisableTabletTweaks);
            Frequency = m.Frequency;
            name = m.name;

            SetValues();

            timeScale = config!.TimeScale;

            frameShiftU = frameShift;
            rInnerU = rInner;
            stockWeightU = stockWeight;
            smoothDistU = smoothDist;
            sepMultU = sepMult;
            aResponseU = aResponse;
            dacOuterU = dacOuter;
            areaScaleU = areaScale;
            xModU = xMod;

            timeScaleU = timeScale;
        }

        public void Initialize(ITabletReport report) 
        {
            Double2 rPos = new Double2(report.Position);
            if (interp) {
                if (tabletToggle)
                    IDTablet(name, ref tabletType);
                    

                if (msOverride > 0) {
                    reportMsAvg = msOverride;
                    msAvg = msOverride;
                    correctWeight = startCorrectWeight * expect * (msStandard / msOverride);
                    secAvg = reportMsAvg / 1000.0;
                    rpsAvg = 1.0 / secAvg;
                }

                if (tabletType == 6 && (savedFilter.msOverride > 0 || (ExGate && config!.Enabled && config!.MsOverrideHover > 0))) {
                    Log.Write("Multifilter", "This tablet's report rate may change when pressing pen/aux buttons. Be sure about using an override setting.", LogLevel.Warning, false, false);
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

            if (dacOuter == 0) 
                adjdWeight = correctWeight * 0.01;

            ResetValues(new Double2(rPos.X * xModU, rPos.Y));


            emergency = 3;
            eflag = false;
        }
        Double2 fuh, fuh2;
        public void HandleConsume(ITabletReport report)
        {
            if (ExGate && config!.Enabled && config!.HoverSettingsEnabled) {
                if (report.Pressure == 0 && (pressure[0] > 0 || gtick == 0)) {
                    HoverSettings();
                }
                if (report.Pressure > 0 && pressure[0] == 0) {
                    DragSettings();
                }
            }

            if (tick < long.MaxValue) tick++;
            if (gtick < long.MaxValue) gtick++;
            if (interp && (tabletType == 1 || tabletType == 2) && (report.Pressure == 0 && pressure[0] > 0) && (pos[0].Y == report.Position.Y)) {   // An extra report with identical position is thrown in. Don't process it.
                InsertAtFirst(pressure, report.Pressure);
                eflag = true;   
                emPos = outputInternal;
                emergency = 4;

                return;
            }
            if (interp && tabletType == 6) {
                if (auxButtons != null) {
                    for (int i = 0; i < 4; i++) {
                        if (auxButtons[i]) {
                            eflag = false;
                            emergency = 10;
                        }
                    }
                }
                if (report.PenButtons[0] != lastPenButtons[0] || report.PenButtons[1] != lastPenButtons[1]) {
                    eflag = false;
                    emergency = 10;
                }
                lastPenButtons = (bool[])report.PenButtons.Clone();
            }

            if (etick < long.MaxValue) etick++;

            consume = true;

            reportTime = (double)reportStopwatch.Restart().TotalMilliseconds;
            consumeDelta = reportTime / 1000.0;

            if (reportTime < 25.0 && reportTime > 0.01) {
                if (emergency > 0) 
                    emergency--;

                if (interp && msOverride == 0) {
                    reportMsAvg += ((reportTime - reportMsAvg) * 0.1);
                    rpsAvg += (1.0 / (consumeDelta) - rpsAvg) * (1.0 - Math.Exp(-2.0 * (consumeDelta)));
                    secAvg = 1.0 / rpsAvg;
                    msAvg = 1000.0 * secAvg;
                    correctWeight = startCorrectWeight * expect * (msStandard / reportMsAvg);
                }

                if (kf != null && kf2 != null && (tick > 89 || msOverride > 0)) {   
                    if (tabletType == 1 && reportMsAvg >= 3.75) {
                        kf.SwapID(2, this);
                        kf2.SwapID(2, this);
                    }
                    else if (tabletType == 2 && reportMsAvg < 3.75) {
                        kf.SwapID(1, this);
                        kf2.SwapID(1, this);
                    }
                }
                timeFloor = false;
            }
            else {
                emergency = 4;
                if (interp) {
                    eflag = false;
                    ResetValues(new Double2(report.Position.X * xModU, report.Position.Y));
                }
            }
            StatUpdate(report);
            if (tick > 90) {
         //   fuh += (PathDiff(pos[1], pos[0], c1p[0]));
         //   fuh2 += (PathDiff(pos[1], pos[0], c2p[0]));
         //   Console.WriteLine(fuh);
        //   Console.WriteLine(fuh2);
            //Console.WriteLine(CrossNorm(dir[0], dir[1], 0));
        //    Console.WriteLine("----");
            }


            if (tabletType == 1 && emergency == 0 && tick > 5) {
                altTime = (double)altTimingStopwatch.Restart().TotalMilliseconds;
                tOverride = 1.0 + Math.Max(0.0, lastTime + (altTime / (msAvg * ((msOverride == 0 || etick < 50) ? 1.001 : 1.0))) - ((etick < 50) ? 2.1 : 2.0));
                
            //    Console.WriteLine(lastTime);

                if (tOverride <= 1.05) {
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

                if (tOverride > 2) {
                    tOverride -= (Math.Floor(tOverride) - 1.0);
                }

                lastTime = tOverride;
            }

            if (tick > 90) {
                

                s1 += Double2.Distance(c1p[0], smpos[0]);
              s2 += Double2.Distance(c2p[0], smpos[0]);

                //Console.WriteLine(s1);
           //    Console.WriteLine(s2);
            //    Console.WriteLine("----");
               
                //Console.WriteLine(c1p[0] - smpos[0]);
            
            }

            ConsumeFilterPass(report);

          //  PointGraph("a", pos[0], gtick);
          //  PointGraph("x", c1p[0], gtick);
       // PointGraph("y", c2p[0], gtick);
        }

        double s1, s2, s3, s4;

        public void HandleUpdate(ITabletReport report) 
        {
            updateTime = (double)updateStopwatch.Restart().TotalMilliseconds;
            altTime = (double)altTimingStopwatch.Restart().TotalMilliseconds;
            if (emergency > 0) {
                report.Pressure = pressure[0];
                lastTime = 0;
                etick = 0;

                if (eflag) {
                    if (!pointFlag) {
                        startOutput = pos[0];
                        FilterPass();
                    }                
                    double eTime = ((double)reportStopwatch.Elapsed.TotalSeconds * Frequency / reportMsAvg) * (expect);
                    double scale = Math.Min((((double)(4 -  emergency) + Math.Min(eTime, 1.0)) * 0.25), 1.0);
                    outputInternal = Double2.Lerp(emPos, adaptOutput, scale); 
                    unconvertedOutput = new Double2(outputInternal.X / xModU, outputInternal.Y);
                    dirOfOutput = (unconvertedOutput - lastOutputPos);
                    report.Position = unconvertedOutput.AsVector2();
                    lastOutputPos = unconvertedOutput;
                }
                else { 
                    ERefresh();
                    emPos = pos[0];
                    unconvertedOutput = new Double2(adaptOutput.X / xModU, adaptOutput.Y);
                    report.Position = unconvertedOutput.AsVector2();
                    lastOutputPos = unconvertedOutput;
                }

                return;
            } 

            double t = 1 + (double)(runningStopwatch.Elapsed - latestReport).TotalSeconds * rpsAvg;

            t = Math.Clamp(t, 0.0, 3.0);

            if (tabletType == 1 && emergency == 0 && tick > 5) {
                tOverride += (altTime) / (msAvg * ((msOverride == 0 || etick < 50) ? 1.001 : 1.0));
                t = tOverride;
                lastTime = t;
            }

            startOutput = Trajectory(stpos[0], stpos[1], stpos[2], t);
            
            if (!pointFlag) {
                FilterPass();
            }


            outputInternal = adaptOutput;

            emPos = outputInternal;
            unconvertedOutput = new Double2(outputInternal.X / xModU, outputInternal.Y);
            report.Position = unconvertedOutput.AsVector2();
            dirOfOutput = (unconvertedOutput - lastOutputPos);
            /*Console.Write("v");
            Console.WriteLine(dirOfOutput.Length());
            Console.Write("a");
            Console.WriteLine((startOutput - ls).Length());
            Console.WriteLine("x");
            Console.WriteLine("d");*/
            lastOutputPos = unconvertedOutput;
            report.Pressure = pressure[0];   
            ls = startOutput;

            if (!(unconvertedOutput + startOutput + clampOutput + smoothOutput + adaptOutput + outputInternal).IsFinite()) {
                ERefresh();
                emPos = pos[0];
                eflag = false;
                emergency = 5;
                ResetValues(pos[0]);
                unconvertedOutput = new Double2(outputInternal.X / xModU, outputInternal.Y);
                report.Position = unconvertedOutput.AsVector2();
            }       

            consume = false;
        }

        Double2 ls;

        public void StatUpdate(ITabletReport report) 
        {
            InsertAtFirst(pos, new Double2(report.Position));
            InsertAtFirst(rawpos, new Double2(report.Position));
            InsertAtFirst(rawdir, rawpos[0] - rawpos[1]);
            pos[0].X *= xModU;
            Double2 smoothed = pos[0];

            if ((savedFilter.reverseSmoothing < 1 && savedFilter.reverseSmoothing > 0) || (ExGate && config!.Enabled && config!.ReverseEmaHover < 1 && config!.ReverseEmaHover > 0)) {
                double brs = reverseSmoothing;
                if ((rawdir[0].Length() <= 1.42 && rawdir[1].Length() <= 1.42) || (tabletType == 6 && (pressure[0] == 0 && (rawdir[0].Length() < 6 || rawdir[1] == Double2.Zero  || rawdir[2] == Double2.Zero  || rawdir[3] == Double2.Zero)))) {
                    brs = 1;
                }

                if (brs >= rs) rs = brs;
                else {
                    double tScale = 0.1 + 0.4 * Smoothstep(rawdir[0].Length(), 6, (pressure[0] > 0 ? 25 : 50));
                    tScale *= 1.0 + Smoothstep(rawdir[0].Length() - rawdir[1].Length(), 0, (pressure[0] > 0 ? 10 : 20));
                    rs = tScale * brs + (1.0 - tScale) * rs;
                    if (rs < brs * (1.0 + tScale / (pressure[0] > 0 ? 5.0 : 10.0))) rs = brs;
                }
                smoothed = pos[1] + (pos[0] - pos[1]) / rs;
            }

            InsertAtFirst(smpos, smoothed);
            InsertAtFirst(dir, smpos[0] - smpos[1]);
            InsertAtFirst(vel, dir[0].Length());
            InsertAtFirst(ddir, dir[0] - dir[1]);
            InsertAtFirst(accel, vel[0] - vel[1]);
            InsertAtFirst(jerk, accel[0] - accel[1]);
            InsertAtFirst(pointaccel, ddir[0].Length());
            InsertAtFirst(pressure, report.Pressure);
            InsertAtFirst(cross, Double2.CrossOfNormalized(dir[0], dir[1]));

            if (config!.Enabled)
                ScaleValues(); 

            if (interp) {
                if (dir[0] == pos[0]) {
                    emergency = 5;
                    dir[0] = Double2.Zero;
                    eflag = false;
                }
                else if (((tabletType == 1 || tabletType == 2 || tabletType == 5) && (pressure[0] > 0 && pressure[1] == 0)) || (tabletType == 5 && (pressure[0] == 0 && pressure[1] > 0))) {
                    if (emergency == 0) 
                        eflag = true;

                    emPos = outputInternal;
                    emergency = 5;
                }
            }
        }

        double s;

        void ConsumeFilterPass(ITabletReport report) 
        {
            if (interp) {
                PredictPass();    
                tOffset += secAvg - consumeDelta;
                tOffset *= Math.Exp(-5.0 * consumeDelta);
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
            Double2 predict = smpos[0];

            if ((savedFilter.frameShift > 0 || (ExGate && config!.Enabled && config!.PredictionRatioHover > 0)) && kf != null && kf2 != null) {
                nonconf = ((emergency > 0) || (tabletType == 1 || tabletType == 2 || tabletType == 5) && ((pressure[0] == 0 && Double2.Distance(dir[0], dir[1] + ddir[1]) > (vel[0] / 8))));
             
                if (emergency <= 1 || eflag) {
                    predict = kf.Update(smpos[0], this);
                }
                else {
                    kf = new KalmanDouble2(smpos[0], this);
                }
                
                InsertAtFirst(rk, predict);

                if (!nonconf) {
                    double fac = Smoothstep(Double2.Distance(dir[0], dir[2]), 5.0, 0.0) * Smoothstep(vel[0], 0.0, 5.0);
                    double faca = Smoothstep(Double2.Distance(dir[0], dir[2]), 5.0, 0.0) * Smoothstep(vel[0], 0.0, 5.0);
                    predict = Double2.Lerp(predict, Double2.Lerp(smpos[0], crpos[0], 0.45) + dir[0], fac);

                    if ((tabletType == 1 || tabletType == 2 || tabletType == 4 || tabletType == 6) && (Double2.Distance(dir[0], dir[2]) > Double2.Distance(dir[0], dir[1])) && (vel[0] > 20.0)) {
                        double frame1 = tabletType switch {
                            1 => 3.0 + Smoothstep(vel[0], 500.0, 150.0) * (0.1 * Smoothstep(jerk[0], 10, 50) - 1.0 * Smoothstep(jerk[0], -10.0, -50.0)),
                            2 or 4 => 3.0 + Smoothstep(vel[0], 500.0, 150.0) * (0.1 * Smoothstep(jerk[0], 20, 50) - 0.3 * Smoothstep(jerk[0], -20.0, -50.0)),
                            _ => 3.0,
                        };

                        double velReq = (tabletType == 6) ?
                        Smoothstep(vel[0], 50.0, 150.0) * Smoothstep(Math.Abs(jerk[0]) + Math.Abs(accel[0]), 10.0, 35.0) :
                        1;
                        
                        Double2 x1 = Trajectory(dir[0], dir[1], dir[2], frame1);

                        double f1 = velReq * Smoothstep(vel[0] + Double2.Distance(dir[0], dir[2]), 0.0, 100.0) * Smoothstep(((dir[0] - dir[1]) + (dir[1] - dir[2])).Length(), 10.0, 50.0);

                        predict = Double2.Lerp(predict, Double2.Lerp(smpos[0], crpos[0], 0.585 - 0.05 * Smoothstep((Math.Abs(accel[0]) + Math.Abs(jerk[0])) * spro(vel[0] / 100.0), 0.0, 250.0)) + x1, Math.Max(0.0, (0.725 + 0.025 * Smoothstep(vel[0] + Math.Abs(accel[0]) + Math.Abs(jerk[0]), 50.0, 150.0)) * f1));

                        if (Double2.DotOfNormalized(dir[0], dir[5]) > 0.9) {
                            Double2 x2 = Trajectory((dir[0] + dir[1]) * 0.5, (dir[2] + dir[3]) * 0.5, (dir[4] + dir[5]) * 0.5, 2.75); 
                            double f2 = velReq * Smoothstep(vel[0] + Math.Abs(accel[0]), 0.0, 100.0) * Smoothstep(vel[0] + Double2.Distance(dir[0], dir[1]), 10.0, 30.0) * Smoothstep(Double2.Distance(dir[0], dir[2]), 3.0, 20.0) * Smoothstep(Math.Abs(accel[0]) + Math.Abs(jerk[0]), 25.0, 10.0);

                            predict = Double2.Lerp(predict, Double2.Lerp(smpos[0], rk[1], 0.35 * Smoothstep(Math.Abs(accel[0]), 25.0, 5.0)) + x2, Math.Max(0.0, 0.5 * (f2 - f1)));

                            if (tabletType == 1) {
                                Double2 x3 = dir[0] + Trajectory((ddir[0] + ddir[1]) * 0.5, (ddir[2] + ddir[3]) * 0.5, (ddir[4] + ddir[5]) * 0.5, 2.75); 
                                double f3 = Smoothstep(Double2.Distance(ddir[0], ddir[5]), 10.0, 25.0) * Smoothstep(vel[0] * spro(Math.Abs(jerk[0]) / 10), 100.0, 425.0);
                                predict = Double2.Lerp(predict, Double2.Lerp(smpos[0], crpos[0], 0.20) + x3, Math.Max(0.0, (0.4 + 0.2 * Smoothstep(vel[0], 100.0, 500.0)) * f3 - 0.5 * f1 - 0.6 * f2));
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
                    Double2 kv = kvf!.Update(dir[0], this);
                        if (etick > 10) {
                            double kvv = Smoothstep(vel[0], 10 * areaScaleU, 30 * areaScaleU) * Smoothstep(Math.Abs(accel[0]) + Math.Abs(jerk[0]), 20.0 * areaScaleU, 5.0 * areaScaleU) * Smoothstep(Double2.Distance(dir[0], dir[5]), 5.0 * areaScaleU, 10.0 * areaScaleU) * Smoothstep((ddir[0] + ddir[1] + ddir[2] + ddir[3] + ddir[4] + ddir[5]).Length(), 30.0 * areaScaleU, 25.0 * areaScaleU);
                    
                                kvv *= (tabletType == 1) ? 0.75 : 0.5;
                                
                                if (kvv > kvw)  {
                                    kvw = 0.25 * kvv + 0.75 * kvw;
                                }
                                else {
                                    kvw = kvv;
                                }
                                InsertAtFirst(c1d, Double2.Lerp(crdir[0], kv, kvw));
                                InsertAtFirst(c1p, crpos[1] + c1d[0] * (1.0 + 0.01 * Smoothstep(jerk[0], 10, 50) - 0.01 * Smoothstep(jerk[0], -10, -50)));
                                if (tabletType == 1 && pressure[0] > 0) {
                                    s = 0;
                                    for (int i = 0; i < HMAX; i++) {
                                        s += cross[i];
                                    }
                                    if (vel[0] > 50) c1p[0] -= (0.7 + 0.5 * Smoothstep(vel[0], 50, 450)) * Step(Math.Abs(s) * Double2.Distance(dir[0], dir[5]), -10, 10) * Double2.Rotate(new Double2(0, s), dir[0].Angle());
                                }
                        }
                        else {
                            InsertAtFirst(c1p, crpos[0]);
                            InsertAtFirst(c1d, crdir[0]);
                        }
                }
                else {
                    InsertAtFirst(c1p, crpos[0]);
                    InsertAtFirst(c1d, crdir[0]);
                }

                if ((tabletType == 1 || tabletType == 2) && (pressure[0] == 0 && (vel[1] == 0))) {
                    c1p[0] = c1p[1];
                }


                

/*
                Double2 c2 = smpos[0];
                if (emergency <= 1 || eflag) {
                    c2 = kf2.Update2(smpos[0], this);
                }
                else {
                    kf2 = new KalmanDouble2(smpos[0], this);
                }
                
                InsertAtFirst(rk2, c2);

                if (!nonconf) {
                    double fac = Smoothstep(Double2.Distance(dir[0], dir[2]), 5.0, 0.0) * Smoothstep(vel[0], 0.0, 5.0);
                    double faca = Smoothstep(Double2.Distance(dir[0], dir[2]), 5.0, 0.0) * Smoothstep(vel[0], 0.0, 5.0);
                    c2 = Double2.Lerp(c2, Double2.Lerp(smpos[0], capos[0], 0.45) + dir[0], fac);

                    if (((tabletType == 1) || tabletType == 2 || tabletType == 4 || tabletType == 6) && (Double2.Distance(dir[0], dir[2]) > Double2.Distance(dir[0], dir[1])) && (vel[0] > 20.0)) {
                        double frame1 = tabletType switch {
                            1 => 3.0 + Smoothstep(vel[0], 500.0, 150.0) * (0.1 * Smoothstep(jerk[0], 10, 50) - 1 * Smoothstep(jerk[0], -10, -50)),
                            2 or 4 => 3.0 + Smoothstep(vel[0], 500.0, 150.0) * (0.1 * Smoothstep(jerk[0], 20, 50) - 0.3 * Smoothstep(jerk[0], -20, -50)),
                            _ => 3.0,
                        };

                        double velReq = (tabletType == 6) ?
                        Smoothstep(vel[0], 50, 150) * Smoothstep(Math.Abs(jerk[0]) + Math.Abs(accel[0]), 10, 35) :
                        1;
                        
                        Double2 x1 = Trajectory(dir[0], dir[1], dir[2], frame1);

                        double f1 = velReq * Smoothstep(vel[0] + Double2.Distance(dir[0], dir[2]), 0.0, 100.0) * Smoothstep(((dir[0] - dir[1]) + (dir[1] - dir[2])).Length(), 10.0, 50.0);

                        c2 = Double2.Lerp(c2, Double2.Lerp(smpos[0], capos[0], 0.585 - 0.05 * Smoothstep((Math.Abs(accel[0]) + Math.Abs(jerk[0])) * spro(vel[0] / 100), 0.0, 250.0)) + x1, Math.Max(0.0, (0.725 + 0.025 * Smoothstep(vel[0] + Math.Abs(accel[0]) + Math.Abs(jerk[0]), 50.0, 150.0)) * f1));

                        if (Double2.DotOfNormalized(dir[0], dir[5]) > 0.9) {
                            Double2 x2 = Trajectory((dir[0] + dir[1]) * 0.5, (dir[2] + dir[3]) * 0.5, (dir[4] + dir[5]) * 0.5, 2.75); 
                            double f2 = velReq * Smoothstep(vel[0] + Math.Abs(accel[0]), 0.0, 100.0) * Smoothstep(vel[0] + Double2.Distance(dir[0], dir[1]), 10.0, 30.0) * Smoothstep(Double2.Distance(dir[0], dir[2]), 3.0, 20.0) * Smoothstep(Math.Abs(accel[0]) + Math.Abs(jerk[0]), 25.0, 10.0);

                            c2 = Double2.Lerp(c2, Double2.Lerp(smpos[0], rk[1], 0.35 * Smoothstep(Math.Abs(accel[0]), 25.0, 5.0)) + x2, Math.Max(0.0, 0.5 * (f2 - f1)));

                            if (tabletType == 1) {
                                Double2 x3 = dir[0] + Trajectory((ddir[0] + ddir[1]) * 0.5, (ddir[2] + ddir[3]) * 0.5, (ddir[4] + ddir[5]) * 0.5, 2.75); 
                                double f3 = Smoothstep(Double2.Distance(ddir[0], ddir[5]), 10.0, 25.0) * Smoothstep(vel[0] * spro(Math.Abs(jerk[0]) / 10), 100.0, 425.0);
                                c2 = Double2.Lerp(c2, Double2.Lerp(smpos[0], capos[0], 0.20) + x3, Math.Max(0.0, (0.4 + 0.2 * Smoothstep(vel[0], 100.0, 500.0)) * f3 - 0.5 * f1 - 0.6 * f2));
                            }
                        }              
                        
                    }
                }

                if (pointFlag && emergency > 0) {
                    c2 = smpos[0];
                }

                InsertAtFirst(capos, c2);
                InsertAtFirst(cadir, capos[0] - capos[1]);

            //    Console.WriteLine(smpos[0]);
            //    Console.WriteLine(DirRelAdd(smpos[0], new Double2(1, 0)));

                if (!nonconf && tabletType == 1 || tabletType == 2 || tabletType == 4) {
                    Double2 kv2 = kvf2!.Update(dir[0], this);
                        if (etick > 10) {
                            double kvv2 = Smoothstep(vel[0], 10 * areaScale, 30 * areaScale) * Smoothstep(Math.Abs(accel[0]) + Math.Abs(jerk[0]), 20.0 * areaScale, 5.0 * areaScale) * Smoothstep(Double2.Distance(dir[0], dir[5]), 5.0 * areaScale, 10.0 * areaScale) * Smoothstep((ddir[0] + ddir[1] + ddir[2] + ddir[3] + ddir[4] + ddir[5]).Length(), 30.0 * areaScale, 25 * areaScale);
                    
                                kvv2 *= (tabletType == 1) ? 0.75 : 0.5;
                                
                                if (kvv2 > kvw2)  {
                                    kvw2 = 0.25 * kvv2 + 0.75 * kvw2;
                                }
                                else {
                                    kvw2 = kvv2;
                                }
                                InsertAtFirst(c2d, Double2.Lerp(cadir[0], kv2, kvw2));
                                InsertAtFirst(c2p, capos[1] + c2d[0] * (1.0 + 0.01 * Smoothstep(jerk[0], 10, 50) - 0.01 * Smoothstep(jerk[0], -10, -50)));
                                if (tabletType == 1) {
                                    s = 0;
                                    for (int i = 0; i < HMAX; i++) {
                                        s += cross[i];
                                    }

                                    if (vel[0] > 50) c2p[0] -= (0.7 + 0.5 * Smoothstep(vel[0], 50, 450)) * Step(Math.Abs(s) * Double2.Distance(dir[0], dir[5]), -10, 10) * Double2.Rotate(new Double2(0, s), dir[0].Angle());
                                }

                        }
                        else {
                            InsertAtFirst(c2p, capos[0]);
                            InsertAtFirst(c2d, cadir[0]);
                        }
                }
                else {
                    InsertAtFirst(c2p, capos[0]);
                    InsertAtFirst(c2d, cadir[0]);
                }

                if ((tabletType == 1 || tabletType == 2) && (pressure[0] == 0 && (vel[1] == 0))) {
                    c2p[0] = c2p[1];
                }
*/

                predict = c1p[0];

                predict += (smpos[0] - predict) * (1 - frameShiftU);

                Console.WriteLine("--" + Double2.Distance(smpos[0], predict));

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

            if ((tabletType == 1 || tabletType == 6) && (Double2.Distance(prpos[0], smpos[0]) > 500.0 + dir[0].Length())) {
                emergency = 5;
            }

        }

        void DAC() 
        {
            if (dacOuterU > 0) {
                double vscale = Smoothstep(vel[0], 5, 10 + dacOuterU);
                double scale = Math.Pow(Smoothstep(Math.Max(pointaccel[0], Double2.Distance(stdir[0], dir[0])), -0.01, (vscale * dacOuterU)), 3.0);
                adjdWeight = correctWeight * Smoothstep(vel[0], 5, 10) * Math.Clamp(scale + 1 - vscale, 0.25, 1);
                Double2 stabilized = Double2.Lerp(stdir[0], svdir[0], 0.9);  
                InsertAtFirst(stdir, stabilized);
                Double2 stpoint = stpos[0] + stdir[0];
                InsertAtFirst(stpos, stpoint);

                double tdWeight = (ExGate && config!.Enabled && (timeScale != 1.0)) ? 
                    UAdjust(adjdWeight, timeScale) :
                    adjdWeight;

                stpos[0] = Double2.Lerp(stpos[0], svpos[0], 0.00001);

                if (vel[0] < 1.5 && (Double2.Distance(stpos[0], svpos[0]) < 5)){ 
                    stpos[0] = svpos[0];
                }

                if (etick < 50 && eflag == false) {
                    stpos[0] = svpos[0];
                }

                Console.WriteLine(Double2.Distance(stpos[0], svpos[0]));
            }
            else {
                InsertAtFirst(stpos, svpos[0]);
                InsertAtFirst(stdir, svdir[0]);
            }
        }

        void RF() 
        {
            Double2 dist = startOutput - clampHold;
            double distLength = dist.Length();
            Double2 ringDir = Math.Max(0, distLength - (rInnerU)) * dist.Normalize();
            double ringDirLength = ringDir.Length();
            clampHold += ringDir;
            clampOutput += ringDir;


            if (ringDirLength > 0 || distLength > rInnerU || accel[0] < -10 * areaScaleU || vel[0] > 10 * rInnerU) {
                double xwa = XWA(expect, updateTime, wireAdjustFlag, reportMsAvg, expect, pointFlag);

                double txwa = (ExGate && config!.Enabled && (timeScale != 1.0)) ? 
                    xwa * timeScale :
                    xwa;

                clampOutput = Double2.Lerp(clampOutput, startOutput, UAdjust(Smoothstep(ringDirLength, -0.01, rInnerU), txwa));
                clampOutput = Double2.Lerp(clampOutput, startOutput, UAdjust(Smoothstep(accel[0], -10 * areaScaleU, -150 * areaScaleU), txwa));
                clampOutput = Double2.Lerp(clampOutput, startOutput, UAdjust(Smoothstep(Double2.Distance(clampOutput, startOutput), 5.0, 0.5), txwa));
            }
        }

        public HPETDeltaStopwatch fStopwatch = new HPETDeltaStopwatch();

        void AEMA() 
        {
            Double2 dist = clampOutput - smoothHold;
            double distLength = dist.Length();
            double mLength = DSFunction(distLength);
            double wcon = WireWeightAdjust(stockWeightU * Default(mLength / distLength, 0), expect, updateTime, wireAdjustFlag);

            double twcon = (ExGate && config!.Enabled && (timeScaleU != 1.0)) ? 
                UAdjust(wcon, timeScaleU) :
                wcon;

            smoothHold += twcon * dist;
            smoothOutput = smoothHold;


            if (sepMultU > 0 && mLength > 0) {
                if (!(wireFlag) || updateTime / expect > 0.99) 
                    sepScale = Smoothstep(distLength, -0.01, smoothDistU * sepMultU);
                
                smoothOutput = Double2.Lerp(smoothHold, Double2.Lerp(smoothHold, clampOutput, stockWeightU), sepScale);
            }

                Console.WriteLine(Double2.Distance(clampOutput, smoothOutput));

           // Console.WriteLine(Double2.Distance(smoothOutput, clampOutput) * (2560.0 / 33020.0));
          //  Console.WriteLine(vel[0] * (2560.0 / 33020.0));
          //  Console.WriteLine("---");

            if (aResponseU > 0) {
                double aDist = Double2.Distance(smoothOutput, adaptOutput);
                double aMod = (1 + Math.Log10(Math.Max(aResponseU, 1))) * Math.Pow(Smoothstep(aDist, (accelResponseOuter * aResponseU * areaScaleU) + 100.0, (accelResponseInner * Math.Sqrt(aResponseU * areaScale)) - 1.0) * Smoothstep(accel[0] + Math.Max(0, jerk[0]) / spro(vel[0] / 150), 10 * areaScaleU, 50 * areaScaleU), accelResponsePower + aResponseU * areaScaleU) * (0.5 + 0.5 * Double2.DotOfNormalized(ddir[0], dir[0]));
                double weight = Math.Clamp(1 - aMod, 0, 1);
               weight *= 1.0 - 0.75 * (Smoothstep(aDist, 1000 * areaScaleU, 5000 * areaScaleU) * Smoothstep(vel[0] + accel[0], 250 * areaScaleU, 500 * areaScaleU));

                double tweight = (ExGate && config!.Enabled && (timeScale != 1.0) && (weight != 1.0)) ?
                    UAdjust(weight, timeScaleU) :
                    weight; 

                adaptOutput = Double2.Lerp(adaptOutput, smoothOutput, WireWeightAdjust(tweight, expect, updateTime, wireAdjustFlag));
            }
            else {
                adaptOutput = smoothOutput;
            }

        }

        public double DSFunction(double dist) 
        {
            if (dist >= smoothDistU) 
                return dist - (smoothDistU / 2);

            double x = (dist / smoothDistU);
            return Math.Pow(x, distanceSmoothingPower) * (smoothDistU / 2);
        }

        double change;

        void FilterPass()
        {
            if (rInner > 0)
                RF(); 
            else 
                clampOutput = startOutput;

            AEMA();
        }

        void ResetValues(Double2 p) 
        {
            tick = 0;
            
            if (kf == null) kf = new KalmanDouble2(p, this);
            if (kf2 == null) kf2 = new KalmanDouble2(p, this);
            int hold = tabletType;
            tabletType = 67;
            kvf = new KalmanDouble2(Double2.Zero, this);
            kvf2 = new KalmanDouble2(Double2.Zero, this);
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

            c2p = Enumerable.Repeat(p, HMAX).ToArray();
            capos = Enumerable.Repeat(p, HMAX).ToArray();
            rk = Enumerable.Repeat(p, HMAX).ToArray();
            rk2 = Enumerable.Repeat(p, HMAX).ToArray();
            
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

        Double2 RTrajectory(double t, Double2 v3, Double2 v2, Double2 v1)
        {
            var mid = 0.5 * (v1 + v3);
            var accel = 2 * (mid - v2);
            var vel = 2 * v2 - v3 - mid;

            // if there is acceleration, then start spacing points evenly using integrals
            if (Double2.Dot(accel, accel) > 0.001)
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

                double _arcTar = arcTar * (t - floor);

                for (int _t = 0; _t < steps; _t++)
                {
                    if (arcArr[_t] < _arcTar) continue;
                    t = _t * dt + floor;
                    break;
                }
            }

            return v3 + t * vel + 0.5 * t * t * accel;
        }

        public void IDTablet(string name, ref int tabletType) {
            if (!iflag){
                iflag = true;
                Identify(name, ref tabletType);
                Log.Write("Multifilter", "Tablet: " + name);
                switch (tabletType) {
                    case 1:
                        Log.Write("Multifilter", "Prediction is enhanced heavily.");
                        Log.Write("Multifilter", "Press/lift bugging is mitigated.");
                        if (msOverride == 0) {
                            Log.Write("Multifilter", "Confident timing system is in use (No timing override).");
                            Log.Write("Multifilter", "Consider using 3.3025 for the Expected Milliseconds Per Report setting.");
                        }
                        else {
                            if (msOverride == 3.3025) {
                                Log.Write("Multifilter", "Confident timing system is in use.");
                            }
                            else {
                                Log.Write("Multifilter", "Confident timing system may or may not be in use. You're on your own here.");
                            }
                        }
                        rpsAvg = 302.8;
                        secAvg = 0.0033025;
                        msAvg = 3.3025;
                        reportMsAvg = 3.3025;
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
                    case 6:
                        Log.Write("Multifilter", "Prediction is enhanced.");
                        Log.Write("Multifilter", "Hover bugging when interacting with certain settings is mitigated.");
                        Log.Write("Multifilter", "Button-press bugging is mitigated.");
                        if (savedFilter.reverseSmoothing == 1 || (ExGate && config!.Enabled && config!.ReverseEmaHover == 1)) {
                            Log.Write("Multifilter", "Important: the experience will be largely better if you set a good Reverse EMA setting.");
                            Log.Write("Multifilter", "Check the README/wiki for more info.");
                        }
                    break;
                    default:
                        Log.Write("Multifilter", "No changes to be made.");
                    break;
                }
            }
        }

        public void DragSettings() {
            frameShift = savedFilter.frameShift;
            reverseSmoothing = savedFilter.reverseSmoothing;
            rInner = savedFilter.rInner;
            stockWeight = savedFilter.stockWeight;
            smoothDist = savedFilter.smoothDist;
            sepMult = savedFilter.sepMult;
            aResponse = savedFilter.aResponse;
            dacOuter = savedFilter.dacOuter;
            msOverride = savedFilter.msOverride;
            areaScale = savedFilter.areaScale;
            xMod = savedFilter.xMod;
            
            frameShiftU = frameShift;
            rInnerU = rInner;
            stockWeightU = stockWeight;
            smoothDistU = smoothDist;
            sepMultU = sepMult;
            aResponseU = aResponse;
            dacOuterU = dacOuter;
            areaScaleU = areaScale;
            xModU = xMod;

            if (msOverride > 0) {
                reportMsAvg = msOverride;
                msAvg = msOverride;
                correctWeight = startCorrectWeight * expect * (msStandard / msOverride);
                secAvg = reportMsAvg / 1000;
                rpsAvg = 1 / secAvg;
            }
        }

        public void HoverSettings() {
            
            frameShift = config!.PredictionRatioHover;
            reverseSmoothing = config!.ReverseEmaHover;
            rInner = config!.InnerRadiusHover;
            stockWeight = config!.StockEmaWeightHover;
            smoothDist = config!.DistanceSmoothingHover;
            sepMult = config!.SepMultHover;
            aResponse = config!.AccelResponseHover;
            dacOuter = config!.DirectionalAntichatterHover;
            msOverride = config!.MsOverrideHover;
            areaScale = config!.AreaScaleHover;
            xMod = config!.XModifierHover;
            
            frameShiftU = frameShift;
            rInnerU = rInner;
            stockWeightU = stockWeight;
            smoothDistU = smoothDist;
            sepMultU = sepMult;
            aResponseU = aResponse;
            dacOuterU = dacOuter;
            areaScaleU = areaScale;
            xModU = xMod;

            if (msOverride > 0) {
                reportMsAvg = msOverride;
                msAvg = msOverride;
                correctWeight = startCorrectWeight * expect * (msStandard / msOverride);
                secAvg = reportMsAvg / 1000;
                rpsAvg = 1 / secAvg;
            }
        }

        public double stockWeightU, rInnerU, smoothDistU, sepMultU, aResponseU, dacOuterU, areaScaleU, xModU, frameShiftU, timeScaleU, distanceSmoothingTimeScaleU, accelResponseTimeScaleU;

        public void ScaleValues() {
            ScaleValue(config!.ScaleStockEmaWeightByMovement, ref stockWeightU, stockWeight, config!.StartStockEmaWeightMult, config!.EndStockEmaWeightMult);
            ScaleValue(config!.ScaleInnerRadiusByMovement, ref rInnerU, rInner, config!.StartInnerRadiusMult, config!.EndInnerRadiusMult);
            ScaleValue(config!.ScaleDistanceSmoothingByMovement, ref smoothDistU, smoothDist, config!.StartDistanceSmoothingMult, config!.EndDistanceSmoothingMult);
            ScaleValue(config!.ScaleSepMultByMovement, ref sepMultU, sepMult, config!.StartSepMultMult, config!.EndSepMultMult);
            ScaleValue(config!.ScaleAccelResponseByMovement, ref aResponseU, aResponse, config!.StartAccelResponseMult, config!.EndAccelResponseMult);
            ScaleValue(config!.ScaleDirectionalAntichatterByMovement, ref dacOuterU, dacOuter, config!.StartDirectionalAntichatterMult, config!.EndDirectionalAntichatterMult);
            ScaleValue(config!.ScaleAreaScaleByMovement, ref areaScaleU, areaScale, config!.StartAreaScaleMult, config!.EndAreaScaleMult);
            ScaleValue(config!.ScaleXModifierByMovement, ref xModU, xMod, config!.StartXModifierMult, config!.EndXModifierMult);
            ScaleValue(config!.ScalePredictionRatioByMovement, ref frameShiftU, frameShift, config!.StartPredictionRatioMult, config!.EndPredictionRatioMult);
            ScaleValue(config!.ScaleTimeScaleByMovement, ref timeScaleU, timeScale, config!.StartTimeScaleMult, config!.EndTimeScaleMult);
            ScaleValue(config!.ScaleDistanceSmoothingTimeScaleByMovement, ref distanceSmoothingTimeScaleU, distanceSmoothingTimeScale, config!.StartDistanceSmoothingTimeScaleMult, config!.EndDistanceSmoothingTimeScaleMult);
            ScaleValue(config!.ScaleAccelResponseTimeScaleByMovement, ref accelResponseTimeScaleU, accelResponseTimeScale, config!.StartAccelResponseTimeScaleMult, config!.EndAccelResponseTimeScaleMult);
        }

        public void ScaleValue(int code, ref double value, double stock, double start, double end) {
            value = stock;
            if (code > 0) {
                if ((code & 1) == 1) {
                    value *= double.Lerp(start, end, Smoothstep(vel[0], config!.StartVelocityThreshold, config!.EndVelocityThreshold));
                }
                if ((code & 2) == 2) {
                    value *= double.Lerp(start, end, Smoothstep(accel[0], config!.StartAccelThreshold, config!.EndAccelThreshold));
                }
                if ((code & 4) == 4) {
                    value *= double.Lerp(start, end, Smoothstep(jerk[0], config!.StartJerkThreshold, config!.EndJerkThreshold));
                }
                if ((code & 8) == 8) {
                    value *= double.Lerp(start, end, Smoothstep(Math.Abs(accel[0]), config!.StartAbsAThreshold, config!.EndAbsAThreshold));
                }
                if ((code & 16) == 16) {
                    value *= double.Lerp(start, end, Smoothstep(accel[0] + Math.Abs(jerk[0]), config!.StartAAbsJThreshold, config!.EndAAbsJThreshold));
                }
                if ((code & 32) == 32) {
                    value *= double.Lerp(start, end, Smoothstep(Math.Abs(accel[0]) + Math.Abs(jerk[0]), config!.StartAbsAAbsJThreshold, config!.EndAbsAAbsJThreshold));
                }
            }
        }

        public void SetValues() {
            if (config!.Enabled) {
                distanceSmoothingPower = config!.DistanceSmoothingPower;
                accelResponsePower = config!.AccelResponsePower;
                accelResponseInner = config!.AccelResponseBaseInnerDistanceThreshold;
                accelResponseOuter = config!.AccelResponseBaseOuterDistanceThreshold;
            }
            else {
                distanceSmoothingPower = 2.0;
                accelResponsePower = 2.5;
                accelResponseInner = 500.0;
                accelResponseOuter = 3500.0;
            }
        }
        
        public Multifilter savedFilter;
        public MultifilterConfig? config;
        public bool consume;
        public string name;
        public int tabletType;
        public Double2[] pos = new Double2[HMAX];
        public Double2[] dir = new Double2[HMAX];
        public Double2[] rawpos = new Double2[HMAX];
        public Double2[] rawdir = new Double2[HMAX];
        public Double2[] ddir = new Double2[HMAX];
        public Double2[] fipos = new Double2[HMAX];
        public Double2[] prpos = new Double2[HMAX];
        public Double2[] crpos = new Double2[HMAX];
        public Double2[] crdir = new Double2[HMAX];
        public Double2[] c1d = new Double2[HMAX];
        public Double2[] c1p = new Double2[HMAX];
        public Double2[] rk = new Double2[HMAX];


        public Double2[] capos = new Double2[HMAX];
        public Double2[] cadir = new Double2[HMAX];
        public Double2[] c2d = new Double2[HMAX];
        public Double2[] c2p = new Double2[HMAX];
        public Double2[] rk2 = new Double2[HMAX];

        public bool ExGate;
        public Double2[] prdir = new Double2[HMAX];
        public Double2[] stpos = new Double2[HMAX];
        public Double2[] stdir = new Double2[HMAX];
        public Double2[] smpos = new Double2[HMAX];
        public Double2[] svpos = new Double2[HMAX];
        public Double2[] svdir = new Double2[HMAX];
        public bool[]? auxButtons;
        public bool[] lastPenButtons = new bool[HMAX];
        public Double2 smoothHold, emPos;
        public double[] vel = new double[HMAX];
        public double[] accel = new double[HMAX];
        public double[] jerk = new double[HMAX];
        public double[] pointaccel = new double[HMAX];
        public double[] cross = new double[HMAX]; 
        public uint[] pressure = new uint[HMAX];
        public Double2 startOutput, outputInternal;
        public Double2 lastOutputPos, dirOfOutput;
        public Double2 unconvertedOutput;
        public double reportTime;
        public double adjdWeight;
        public double correctWeight;
        public bool init = false;
        public bool altTimeWarn;
        public int emergency;
        public double reportMsAvg;
        public double sepScale;
        public double kvw;
        public double kvw2;
        public double rs;
        public double lastTime;
        public double tOverride;
        public bool iflag;
        public bool timeFloor;
        public double altTime;
        public const double startCorrectWeight = 0.1;    
        public const double msStandard = 3.302466;
        public double expect => 1000 / Frequency;
        public long tick = 0;
        public long etick = 0;
        public long ttick = 0;
        public long gtick = 0;
        public HPETDeltaStopwatch reportStopwatch = new HPETDeltaStopwatch();
        public HPETDeltaStopwatch updateStopwatch = new HPETDeltaStopwatch();
        public HPETDeltaStopwatch altTimingStopwatch = new HPETDeltaStopwatch();
        public KalmanDouble2? kf;
        public KalmanDouble2? kvf;
        public KalmanDouble2? kf2;
        public KalmanDouble2? kvf2;
        public TimeSpan latestReport = TimeSpan.Zero;
        public double rpsAvg = 200, tOffset;
        public double msAvg = 5;
        public double secAvg = 0.005;
        public double consumeDelta;
        public HPETDeltaStopwatch runningStopwatch = new HPETDeltaStopwatch(true);
        private static readonly int steps = 256;
        private static readonly double dt = 1 / steps;
        private double[] arcArr = new double[steps];
        private double arcTar = 0;
        private Double2 _v1, _v2, _v3;
        private int _floor;

        public double frameShift, reverseSmoothing, dacOuter;
        public string wireMode;
        public double rInner, stockWeight, smoothDist, sepMult, aResponse, msOverride, areaScale, xMod;
        public bool tabletToggle;
        public Double2 clampHold, clampOutput;
        public Double2 smoothOutput;
        public Double2 adaptOutput;



        public double distanceSmoothingPower;
        public double accelResponsePower;
        public double accelResponseInner;
        public double accelResponseOuter;
        public double timeScale;
        public double distanceSmoothingTimeScale;
        public double accelResponseTimeScale;

        public bool interp;
        public int wireCode;
        public double updateTime;
        public bool wireFlag, pointFlag, wireAdjustFlag, eflag, nonconf;
        public double Frequency;
    }
}