//-----------------------------------------------------------------------
// <copyright file="QueryAfterAdHocIndexIsReset.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using Newtonsoft.Json.Linq;
using Raven.Database.Data;
using Raven.Database.Queries;
using Xunit;
using System.Linq;

namespace Raven.Tests.Bugs
{
    public class QueryAfterAdHocIndexIsReset : LocalClientTest
    {
        [Fact]
        public void ShouldStillWork()
        {
            using(var store = NewDocumentStore())
            {
                store.DatabaseCommands.Put("ayende", null, new JObject{ {"Name", "Ayende"}}, new JObject());
            	var baseLineIndexCount = store.DocumentDatabase.GetIndexNames(0, int.MaxValue).Count;

            	var queryResult = store.DatabaseCommands.Query("dynamic", new IndexQuery
                {
                    Query = "Name:Ayende",
                }, new string[0]);

                Assert.NotEmpty(queryResult.Results);

				Assert.Equal(baseLineIndexCount+1, store.DocumentDatabase.GetIndexNames(0, int.MaxValue).Count);
                
                store.DocumentDatabase.StopBackgroundWokers();

                store.Configuration.TempIndexCleanupThreshold = TimeSpan.Zero;

                store.DocumentDatabase.ExtensionsState.Values.OfType<DynamicQueryRunner>().First().CleanupCache();

				Assert.Equal(baseLineIndexCount, store.DocumentDatabase.GetIndexNames(0, int.MaxValue).Count);

                store.Configuration.TempIndexCleanupThreshold = TimeSpan.FromMinutes(5);
 
                store.DocumentDatabase.SpinBackgroundWorkers();

                 queryResult = store.DatabaseCommands.Query("dynamic", new IndexQuery
                {
                    Query = "Name:Ayende",
                }, new string[0]);

                Assert.NotEmpty(queryResult.Results);
            }
        }
    }
}