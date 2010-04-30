﻿using System;
using System.Collections.Generic;
using System.IO;
using Raven.Client.Document;
using Raven.Client.Shard;
using Raven.Client.Shard.ShardStrategy;
using Raven.Client.Shard.ShardStrategy.ShardAccess;
using Raven.Database;
using Raven.Server;

namespace Raven.Sample.ComplexSharding
{
	class Program
	{
		static void Main()
		{
			// start 5 instances of Raven's servers
			Console.WriteLine("Starting...");
			DeleteDirectories("Users", "Blogs", "Posts.1", "Posts.2", "Posts.3");
			var ravenDbServers = StartServers();
			Console.WriteLine("All servers started...");

			var shards = new Shards
			{
				new DocumentStore
				{
					Identifier = "Users",
					Url = "http://localhost:8081",
					Conventions =
						{
							GenerateDocumentKey = user => "users/" + ((User) user).Name
						}
				},
				new DocumentStore {Identifier = "Blogs", Url = "http://localhost:8082"},
				new DocumentStore {Identifier = "Posts #1", Url = "http://localhost:8083"},
				new DocumentStore {Identifier = "Posts #2", Url = "http://localhost:8084"},
				new DocumentStore {Identifier = "Posts #3", Url = "http://localhost:8085"}
			};

			var shardStrategy = new ShardStrategy
			{
				ShardAccessStrategy =new ParallelShardAccessStrategy(),
				ShardSelectionStrategy = new BlogShardSelectionStrategy(3),
				ShardResolutionStrategy = new BlogShardResolutionStrategy(3),
			};
			var documentStore = new ShardedDocumentStore(shardStrategy, shards);
			documentStore.Initialise();

			using(var session = documentStore.OpenSession())
			{
				var user = new User { Name = "ayende" };
				var blog = new Blog { Name = "Ayende @ Rahien" };

				session.Store(user);
				session.Store(blog);

				// we have to save to Raven to get the generated id for the blog instance
				session.SaveChanges();
				var posts = new List<Post>();
				for (var i = 0; i < 6; i++)
				{
					var post = new Post
					{
						BlogId = blog.Id,
						UserId = user.Id,
						Content = "Just a post",
						Title = "Post #" + (i + 1)
					};
					posts.Add(post);
					session.Store(post);
				}

				session.SaveChanges();

				foreach (var post in posts)
				{
					Console.WriteLine(post.Id);
				}
			}


			documentStore.Dispose();

			foreach (var server in ravenDbServers)
			{
				server.Dispose();
			}
		}

		private static IEnumerable<RavenDbServer> StartServers()
		{
			return new[]
			{
				new RavenDbServer(new RavenConfiguration
				{
					DataDirectory = "Users",
					AnonymousUserAccessMode = AnonymousUserAccessMode.All,
					Port = 8081
				}),
				new RavenDbServer(new RavenConfiguration
				{
					DataDirectory = "Blogs",
					AnonymousUserAccessMode = AnonymousUserAccessMode.All,
					Port = 8082
				}),
				new RavenDbServer(new RavenConfiguration
				{
					DataDirectory = "Posts.1",
					AnonymousUserAccessMode = AnonymousUserAccessMode.All,
					Port = 8083
				}),
				new RavenDbServer(new RavenConfiguration
				{
					DataDirectory = "Posts.2",
					AnonymousUserAccessMode = AnonymousUserAccessMode.All,
					Port = 8084
				}),
				new RavenDbServer(new RavenConfiguration
				{
					DataDirectory = "Posts.3",
					AnonymousUserAccessMode = AnonymousUserAccessMode.All,
					Port = 8085
				})
			};
		}

		private static void DeleteDirectories(params string [] dirs)
		{
			foreach (var dir in dirs)
			{
				if(Directory.Exists(dir))
					Directory.Delete(dir, true);
			}
		}
	}
}