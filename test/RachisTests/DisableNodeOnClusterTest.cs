﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FastTests.Server.Replication;
using Raven.Client.Documents;
using Raven.Server.Config;
using Raven.Tests.Core.Utils.Entities;
using Tests.Infrastructure;
using Xunit;

namespace RachisTests
{
    public class DisableNodeOnClusterTest : ReplicationTestBase
    {
        [Fact]
        public async Task BackToFirstNodeAfterRevive()
        {
            var db = "BackToFirstNodeAfterRevive";
            var leader = await CreateRaftClusterAndGetLeader(3, shouldRunInMemory: false);
            await CreateDatabaseInCluster(db, 3, leader.WebUrl);

            using (var leaderStore = new DocumentStore
            {
                Database = db,
                Urls = new[] { leader.WebUrl }
            }.Initialize())
            {
                var re = leaderStore.GetRequestExecutor();
                using (var session = leaderStore.OpenSession())
                {
                    session.Advanced.WaitForReplicationAfterSaveChanges(replicas: 2, timeout: TimeSpan.FromSeconds(30));
                    session.Store(new User
                    {
                        Name = "Idan"
                    });
                    session.SaveChanges();
                }

                var firstNodeUrl = re.Url;
                var firstNode = Servers.Single(s => s.WebUrl == firstNodeUrl);
                var nodePath = firstNode.Configuration.Core.DataDirectory.FullPath.Split('/').Last();
                await DisposeServerAndWaitForFinishOfDisposalAsync(firstNode);

                // check that replication works.
                using (var session = leaderStore.OpenSession())
                {
                    session.Advanced.WaitForReplicationAfterSaveChanges(replicas: 1, timeout: TimeSpan.FromSeconds(30));
                    session.Store(new User
                    {
                        Name = "Karmel"
                    }, "users/1");
                    session.SaveChanges();
                }

                Assert.NotEqual(re.Url, firstNodeUrl);
                var customSettings = new Dictionary<string, string>
                {
                    {RavenConfiguration.GetKey(x => x.Core.ServerUrls), firstNodeUrl},
                };
                Servers.Add(GetNewServer(customSettings, runInMemory: false, deletePrevious: false, partialPath: nodePath));

                Assert.True(WaitForValue(() => firstNodeUrl == leaderStore.GetRequestExecutor().Url, true));
            }
        }
    }
}
