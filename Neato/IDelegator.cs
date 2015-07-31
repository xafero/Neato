using System;
using System.Collections.Generic;

namespace Neato
{
	public interface IDelegator
	{
		
		IDictionary<string, string> Settings { get; }
		
		void Process<TSender, TEvent, TArgs>(TSender s, TEvent e, TArgs a) where TSender : class;
		
	}
}