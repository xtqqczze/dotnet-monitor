﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Monitoring.StartupHook.Exceptions;
using Microsoft.Diagnostics.Monitoring.StartupHook.Monitoring;
using Microsoft.Diagnostics.Monitoring.StartupHook.ParameterCapturing;
using Microsoft.Diagnostics.Tools.Monitor;
using Microsoft.Diagnostics.Tools.Monitor.Profiler;
using Microsoft.Diagnostics.Tools.Monitor.StartupHook;
using System;
using MessageDispatcher = Microsoft.Diagnostics.Monitoring.StartupHook.MonitorMessageDispatcher;

namespace Microsoft.Diagnostics.Monitoring.StartupHook
{
    internal sealed class DiagnosticsBootstrapper :
        IDisposable
    {
        private readonly CurrentAppDomainExceptionProcessor _exceptionProcessor;
        private readonly ParameterCapturingService? _parameterCapturingService;

        private long _disposedState;

        public DiagnosticsBootstrapper()
        {
            _exceptionProcessor = new(ToolIdentifiers.IsEnvVarEnabled(InProcessFeaturesIdentifiers.EnvironmentVariables.Exceptions.IncludeMonitorExceptions));

            // If collectOnStart is not set, assume the default value (true).
            string? collectOnStart = Environment.GetEnvironmentVariable(InProcessFeaturesIdentifiers.EnvironmentVariables.Exceptions.CollectOnStartup);
            if (collectOnStart == null || ToolIdentifiers.IsEnvVarValueEnabled(collectOnStart))
            {
                _exceptionProcessor.Start();
            }

            using IDisposable _ = MonitorExecutionContextTracker.MonitorScope();

            try
            {
                // Check that the profiler is loaded before establishing the dispatcher, which has a dependency on the existence of the profiler
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ProfilerIdentifiers.NotifyOnlyProfiler.EnvironmentVariables.ProductVersion)))
                {
                    SharedInternals.MessageDispatcher = new MessageDispatcher.MonitorMessageDispatcher(
                        new MessageDispatcher.ProfilerMessageSource(CommandSet.StartupHook));
                    ToolIdentifiers.EnableEnvVar(InProcessFeaturesIdentifiers.EnvironmentVariables.AvailableInfrastructure.ManagedMessaging);

                    SharedInternals.MessageDispatcher.RegisterCallback<EmptyPayload>(StartupHookCommand.StopAllFeatures, (IpcMessage) =>
                    {
                        _exceptionProcessor.Stop();
                        _parameterCapturingService?.RequestStopAll();
                    });

                    SharedInternals.MessageDispatcher.RegisterCallback<EmptyPayload>(StartupHookCommand.StartAllFeatures, (IpcMessage) =>
                    {
                        _exceptionProcessor.Start();
                    });
                }

                if (ToolIdentifiers.IsEnvVarEnabled(InProcessFeaturesIdentifiers.EnvironmentVariables.ParameterCapturing.Enable))
                {
                    _parameterCapturingService = new();
                    _parameterCapturingService.Start();
                }
            }
            catch
            {
            }

            ToolIdentifiers.EnableEnvVar(InProcessFeaturesIdentifiers.EnvironmentVariables.AvailableInfrastructure.StartupHook);
        }

        public void Dispose()
        {
            if (!DisposableHelper.CanDispose(ref _disposedState))
                return;

            _exceptionProcessor.Dispose();
            _parameterCapturingService?.Stop();
            _parameterCapturingService?.Dispose();
            SharedInternals.MessageDispatcher?.Dispose();
        }
    }
}
