﻿using System;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Raven.Client.Document;

namespace Raven.Client.Indexes
{
	/// <summary>
	/// Generate index defintion from linq expressions
	/// </summary>
	public class IndexDefinitionHelper
	{

		/// <summary>
		/// Perform the actual generation
		/// </summary>
		public static string PruneToFailureLinqQueryAsStringToWorkableCode(
			LambdaExpression expr,
			DocumentConvention convention,
			string querySource, bool translateIdentityProperty)
		{
			if (expr == null)
				return null;
			var expression = expr.Body;

			switch (expression.NodeType)
			{
				case ExpressionType.ConvertChecked:
				case ExpressionType.Convert:
					expression = ((UnaryExpression)expression).Operand;
					break;
			}

#if !NET_3_5
			var linqQuery = ExpressionStringBuilder.ExpressionToString(convention, translateIdentityProperty, expression);
#else
            var linqQuery =expression.ToString();
#endif

			var querySourceName = expr.Parameters.First(x => x.Type != typeof(IClientSideDatabase)).Name;

			if (linqQuery.StartsWith(querySourceName))
				linqQuery = querySource + linqQuery.Substring(querySourceName.Length);
			else if (linqQuery.StartsWith("(" + querySourceName + ")"))
				linqQuery = querySource + linqQuery.Substring(querySourceName.Length + 2);
			else
				throw new InvalidOperationException("Canot understand how to parse the query");

			linqQuery = ReplaceAnonymousTypeBraces(linqQuery);
			linqQuery = Regex.Replace(linqQuery, @"new ((VB\$)|(<>))[\w_]+`\d+", "new ");// remove anonymous types
			linqQuery = Regex.Replace(linqQuery, @"<>([a-z])_", "__$1_"); // replace <>h_ in transperant identifiers
			const string pattern = @"(\.Where\(|\.Select\(|\.GroupBy\(|\.SelectMany)";
			linqQuery = Regex.Replace(linqQuery, pattern, "\r\n\t$1"); // formatting
			return linqQuery;
		}

		private static string ReplaceAnonymousTypeBraces(string linqQuery)
		{
			const string pattern = @"new ((VB\$)|(<>))[\w_]+`\d+";
			var matches = Regex.Matches(linqQuery, pattern);
			for (int i = 0; i < matches.Count; i++)
			{
				var match = matches[i];
				int endBrace = -1;
				var startBrace = linqQuery[match.Index + match.Length];
				int startIndex = match.Index + match.Length;
				if (startBrace != '(')
					break;

				int otherBraces = 0;
				for (int j = startIndex + 1; j < linqQuery.Length; j++)
				{
					if (linqQuery[j] == '(')
					{
						otherBraces++;
						continue;
					}
					else if (linqQuery[j] != ')')
						continue;
					if (otherBraces == 0)
					{
						endBrace = j;
						break;
					}
					otherBraces--;
				}
				if (endBrace != -1)
				{
					string s = linqQuery.Substring(0, match.Index + match.Length) + "{";
					s += linqQuery.Substring(startIndex + 1, endBrace - startIndex - 1) + "}";
					s += linqQuery.Substring(endBrace + 1);
					linqQuery = s;
					matches = Regex.Matches(linqQuery, pattern);
					continue;
				}
				break;
			}
			return linqQuery;
		}
	}
}