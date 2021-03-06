﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using MigrationTools._EngineV1.Configuration;
using MigrationTools.Endpoints;
using MigrationTools.Enrichers;

namespace MigrationTools.Processors
{
    public abstract class Processor : IProcessor
    {
        private bool _ProcessorConfigured;

        public Processor(
            ProcessorEnricherContainer processorEnrichers,
            EndpointContainer endpoints,
            IServiceProvider services,
            ITelemetryLogger telemetry,
            ILogger<Processor> logger)
        {
            Services = services;
            Telemetry = telemetry;
            Log = logger;
            Endpoints = endpoints;
            ProcessorEnrichers = processorEnrichers;
        }

        public EndpointContainer Endpoints { get; }

        public ProcessorEnricherContainer ProcessorEnrichers { get; }

        public string Name { get { return this.GetType().Name; } }

        public ProcessingStatus Status { get; private set; } = ProcessingStatus.None;
        protected IServiceProvider Services { get; }
        protected ITelemetryLogger Telemetry { get; }
        protected ILogger<Processor> Log { get; }

        public bool SupportsProcessorEnrichers => false;

        public virtual void Configure(IProcessorOptions options)
        {
            Log.LogInformation("Processor::Configure");
            Endpoints.ConfigureEndpoints(options.Source, options.Target);
            ProcessorEnrichers.ConfigureEnrichers(options.ProcessorEnrichers);
            _ProcessorConfigured = true;
        }

        public void Configure(IProcessorConfig config)
        {
            Configure((IProcessorOptions)config);
        }

        public void Execute()
        {
            Telemetry.TrackEvent(this.Name);
            Log.LogInformation("Migration Context Start: {MigrationContextname} ", Name);
            DateTime start = DateTime.Now;
            var executeTimer = Stopwatch.StartNew();
            //////////////////////////////////////////////////
            try
            {
                if (!_ProcessorConfigured)
                {
                    Log.LogError("Processor::Execute: Processer base has not been configured.");
                    throw new InvalidOperationException("Processer base has not been configured.");
                }
                Status = ProcessingStatus.Running;
                InternalExecute();
                Status = ProcessingStatus.Complete;
                executeTimer.Stop();

                Log.LogInformation(" Migration Processor Complete {MigrationContextname} ", Name);
            }
            catch (Exception ex)
            {
                Status = ProcessingStatus.Failed;
                executeTimer.Stop();

                Telemetry.TrackException(ex,
                    new Dictionary<string, string>
                    {
                        {"Name", Name},
                        //{"Target", Engine.Target.Config.ToString()},
                        //{"Source", Engine.Source.Config.ToString()},
                        {"Status", Status.ToString()}
                    },
                    new Dictionary<string, double>
                    {
                        {"MigrationContextTime", executeTimer.ElapsedMilliseconds}
                    });
                Log.LogCritical(ex, "Error while running {MigrationContextname}", Name);
            }
            finally
            {
                Log.LogInformation("{ProcessorName} completed in {ProcessorDuration} ", Name, executeTimer.Elapsed.ToString("c"));
                Telemetry.TrackRequest(this.Name, start, executeTimer.Elapsed, Status.ToString(), (Status == ProcessingStatus.Complete));
            }
        }

        protected abstract void InternalExecute();

        protected Type GetTypeFromName(string name)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                 .Where(a => !a.IsDynamic)
                 .SelectMany(a => a.GetTypes())
                 .FirstOrDefault(t => t.Name.Equals(name) || t.FullName.Equals(name));
            if (type is null)
            {
                var e = new InvalidOperationException("The Type cant be found");
                Log.LogError(e, "Unable to find the type {ObjectToLoadName} needed by the processor {ProfessorName}", name, nameof(this.GetType));
                throw e;
            }
            return type;
        }
    }
}