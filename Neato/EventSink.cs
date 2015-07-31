using System;
using System.Reflection;
using System.Windows;
using System.Collections.Generic;
using Expression = System.Linq.Expressions.Expression;

namespace Neato
{
	public class EventSink
	{
		private readonly IDelegator delegator;
		
		public EventSink(Application app, IDelegator delegator)
		{
			app.Activated += app_Activated;
			this.delegator = delegator;
		}
		
		#region Application events
		private void app_Activated(object sender, EventArgs e)
		{
			Console.WriteLine("Application '{0}' activated.", sender.GetType().FullName);
			// Got application!
			var app = (Application)sender;
			// Collect all events from application
			var ape = CollectEventsAndBind(app);
			Console.WriteLine("Events from application => {0}", string.Join(", ", ape));
			// Get main window
			var mw = app.MainWindow;
			// Collect all events from main window
			var mwe = CollectEventsAndBind(mw);
			Console.WriteLine("Events from main window => {0}", string.Join(", ", mwe));
		}
		#endregion
		
		#region Event helpers
		private ISet<string> CollectEventsAndBind(object obj)
		{
			var eventNames = new SortedSet<string>();
			var type = obj.GetType();
			var flags = BindingFlags.Instance | BindingFlags.NonPublic
				| BindingFlags.FlattenHierarchy | BindingFlags.Public;
			var meth = typeof(IDelegator).GetMethod("Process");
			foreach (var ev in type.GetEvents(flags))
			{
				var sender = Expression.Parameter(typeof(object), "sender");
				var eid = Expression.Constant(ev.Name);
				var evht = ev.EventHandlerType;
				var evt = evht.GetMethod("Invoke").GetParameters()[1].ParameterType;
				var args = Expression.Parameter(evt, "args");
				var gm = meth.MakeGenericMethod(typeof(object), typeof(string), evt);
				var body = Expression.Call(Expression.Constant(delegator), gm, sender, eid, args);
				var lambda = Expression.Lambda(evht, body, sender, args);
				var handler = lambda.Compile();
				ev.AddMethod.Invoke(obj, new object[] { handler });
				eventNames.Add(ev.Name);
			}
			return eventNames;
		}
		#endregion
	}
}