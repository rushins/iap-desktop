﻿
// Copyright 2020 Google LLC
//
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
//

using Google.Apis.Auth.OAuth2;
using Google.Apis.Logging.v2;
using Google.Apis.Logging.v2.Data;
using Google.Solutions.Common.Auth;
using Google.Solutions.Common.Locator;
using Google.Solutions.Common.Test;
using Google.Solutions.Common.Test.Integration;
using Google.Solutions.Common.Util;
using Google.Solutions.IapDesktop.Application.Services.Adapters;
using Google.Solutions.IapDesktop.Application.Services.SecureConnect;
using Google.Solutions.IapDesktop.Application.Services.Settings;
using Google.Solutions.IapDesktop.Extensions.Rdp.Services.Tunnel;
using Google.Solutions.IapTunneling.Iap;
using Microsoft.Win32;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Google.Solutions.IapDesktop.Extensions.Rdp.Test.Services.Tunnel
{
    [TestFixture]
    [Category("IntegrationTest")]
    [Category("SecureConnect")]
    public class TestTunnelServiceWithMtls : FixtureBase
    {
        private const string TestKeyPath = @"Software\Google\__Test";
        private ApplicationSettingsRepository applicationSettingsRepository;

        [SetUp]
        public void SetUp()
        {
            var hkcu = RegistryKey.OpenBaseKey(
                RegistryHive.CurrentUser,
                RegistryView.Default);
            hkcu.DeleteSubKeyTree(TestKeyPath, false);
            this.applicationSettingsRepository = new ApplicationSettingsRepository(
                hkcu.CreateSubKey(TestKeyPath));
        }

        private IAuthorizationAdapter CreateAuthorizationAdapterForSecureConnectUser()
        {
            var authz = new Mock<IAuthorization>();
            authz.SetupGet(a => a.Credential).Returns(TestProject.GetSecureConnectCredential());

            var enrollment = new Mock<IDeviceEnrollment>();
            enrollment.SetupGet(e => e.State)
                .Returns(DeviceEnrollmentState.Enrolled);
            enrollment.SetupGet(e => e.Certificate)
                .Returns(TestProject.GetDeviceCertificate());

            var adapter = new Mock<IAuthorizationAdapter>();
            adapter.SetupGet(a => a.Authorization).Returns(authz.Object);
            adapter.SetupGet(a => a.DeviceEnrollment).Returns(enrollment.Object);

            return adapter.Object;
        }

        private static async Task<IList<LogEntry>> GetIapAccessLogsForPortAsync(ushort port)
        {
            var loggingService = TestProject.CreateService<LoggingService>();
            var request = new ListLogEntriesRequest()
            {
                ResourceNames = new[] { "projects/" + TestProject.ProjectId },
                Filter =
                    "protoPayload.methodName=\"AuthorizeUser\"\n" +
                    $"logName=\"projects/{TestProject.ProjectId}/logs/cloudaudit.googleapis.com%2Fdata_access\"\n" +
                    $"protoPayload.requestMetadata.destinationAttributes.port=\"{port}\"",
                OrderBy = "timestamp desc"
            };

            // Logs take some time to show up, so retry a few times.
            for (int retry = 0; retry < 20; retry++)
            {
                var entries = await loggingService.Entries.List(request).ExecuteAsync();

                if (entries.Entries.EnsureNotNull().Any())
                {
                    return entries.Entries;
                }

                await Task.Delay(1000);
            }

            return new List<LogEntry>();
        }

        [Test]
        public async Task WhenDeviceEnrolled_ThenAuditLogIndicatesDevice(
            [WindowsInstance] ResourceTask<InstanceLocator> testInstance)
        {
            var service = new TunnelService(CreateAuthorizationAdapterForSecureConnectUser());

            // Probe a random port so that we have something unique to look for
            // in the audit log.

            var randomPort = (ushort)new Random().Next(10000, 50000);
            var destination = new TunnelDestination(
                await testInstance,
                randomPort);

            using (var tunnel = await service.CreateTunnelAsync(
                destination,
                new SameProcessRelayPolicy()))
            {
                Assert.AreEqual(destination, tunnel.Destination);
                Assert.IsTrue(tunnel.IsMutualTlsEnabled);

                // The probe will fail, but it will leave a record in the audit log.
                try
                {
                    await tunnel.Probe(TimeSpan.FromSeconds(5));
                }
                catch (UnauthorizedException)
                { }
            }

            var logs = await GetIapAccessLogsForPortAsync(randomPort);
            Assert.IsTrue(logs.Any(), "data access log emitted");

            var metadata = (JToken)logs.First().ProtoPayload["metadata"];
            Assert.AreNotEqual(
                "Unknown",
                metadata.Value<string>("device_state"));
            CollectionAssert.Contains(
                new[] { "Normal", "Cross Organization" }, 
                metadata.Value<string>("device_state"));
        }
    }
}
