﻿namespace Raven.Studio.Features.Statistics
{
	using System.Collections.Generic;
	using System.ComponentModel.Composition;
	using Caliburn.Micro;
	using Plugin;
	using Raven.Database.Data;

	[Export]
	public class ErrorsViewModel : Screen
	{
		readonly IServer server;

		[ImportingConstructor]
		public ErrorsViewModel(IServer server)
		{
			DisplayName = "Errors";
			this.server = server;
			//server.CurrentDatabaseChanged += delegate { NotifyOfPropertyChange( ()=> Errors );};
		}

		public IEnumerable<ServerError> Errors
		{
			get { return server.Statistics.Errors; }
		}

		public IServer Server
		{
			get { return server; }
		}
	}
}