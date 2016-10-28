﻿namespace Unit.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.Extensibility.PerfCounterCollector.Implementation;

    using CounterData = System.Tuple<Microsoft.ApplicationInsights.Extensibility.PerfCounterCollector.Implementation.PerformanceCounterData, System.Collections.Generic.List<float>>;

    /// <summary>
    /// Mock to test clients of PerformanceCollector.
    /// </summary>
    internal class PerformanceCollectorMock : IPerformanceCollector
    {
        public object Sync = new object();

        private readonly List<CounterData> counters = new List<CounterData>();

        public List<CounterData> Counters
        {
            get
            {
                lock (this.Sync)
                {
                    return this.counters;
                }
            }
        }

        public IEnumerable<PerformanceCounterData> PerformanceCounters
        {
            get
            {
                lock (this.Sync)
                {
                    return this.counters.Select(c => c.Item1);
                }
            }
        }

        public void RegisterPerformanceCounter(
            string originalString,
            string reportAs,
            string categoryName,
            string counterName,
            string instanceName,
            bool usesInstanceNamePlaceholder,
            bool isCustomCounter)
        {
            lock (this.Sync)
            {
                this.counters.Add(
                    Tuple.Create(
                        new PerformanceCounterData(
                            originalString,
                            reportAs,
                            usesInstanceNamePlaceholder,
                            isCustomCounter,
                            false,
                            categoryName,
                            counterName,
                            instanceName),
                        new List<float>()));
            }
        }

        public IEnumerable<Tuple<PerformanceCounterData, float>> Collect(Action<string, Exception> onReadingFailure)
        {
            lock (this.Sync)
            {
                foreach (var counter in this.counters)
                {
                    var value = (float)counter.Item1.OriginalString.GetHashCode();

                    var result =
                        Tuple.Create(
                            new PerformanceCounterData(
                                counter.Item1.OriginalString,
                                counter.Item1.ReportAs,
                                counter.Item1.UsesInstanceNamePlaceholder,
                                counter.Item1.IsCustomCounter,
                                counter.Item1.IsInBadState,
                                counter.Item1.CategoryName,
                                counter.Item1.CounterName,
                                counter.Item1.InstanceName),
                            value);

                    counter.Item2.Add(value);

                    yield return result;
                }
            }
        }

        public void RefreshPerformanceCounter(PerformanceCounterData pcd)
        {
        }

        public void RefreshCounters()
        {
        }

        public void RegisterCounter(
            string perfCounterName,
            string reportAs,
            bool isCustomCounter,
            out string error)
        {
            bool usesInstanceNamePlaceholder;
            var pc = this.CreateCounter(
                perfCounterName,
                out usesInstanceNamePlaceholder,
                out error);

            if (pc != null)
            {
                this.RegisterCounter(perfCounterName, reportAs, pc, isCustomCounter, usesInstanceNamePlaceholder, out error);
            }
        }

        public PerformanceCounter CreateCounter(
            string perfCounterName,
            out bool usesInstanceNamePlaceholder,
            out string error)
        {
            error = null;

            try
            {
                return PerformanceCounterUtility.ParsePerformanceCounter(
                    perfCounterName,
                    new string[] { },
                    new string[] { },
                    out usesInstanceNamePlaceholder);
            }
            catch (Exception e)
            {
                usesInstanceNamePlaceholder = false;
                PerformanceCollectorEventSource.Log.CounterParsingFailedEvent(e.Message, perfCounterName);
                error = e.Message;

                return null;
            }
        }

        private void RegisterCounter(
            string originalString,
            string reportAs,
            PerformanceCounter pc,
            bool isCustomCounter,
            bool usesInstanceNamePlaceholder,
            out string error)
        {
            error = null;

            try
            {
                this.RegisterPerformanceCounter(
                    originalString,
                    reportAs,
                    pc.CategoryName,
                    pc.CounterName,
                    pc.InstanceName,
                    usesInstanceNamePlaceholder,
                    isCustomCounter);

                PerformanceCollectorEventSource.Log.CounterRegisteredEvent(
                    PerformanceCounterUtility.FormatPerformanceCounter(pc));
            }
            catch (InvalidOperationException e)
            {
                PerformanceCollectorEventSource.Log.CounterRegistrationFailedEvent(
                    e.Message,
                    PerformanceCounterUtility.FormatPerformanceCounter(pc));
                error = e.Message;
            }
        }
    }
}
