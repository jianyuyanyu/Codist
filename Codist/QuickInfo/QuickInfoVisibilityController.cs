﻿using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AppHelpers;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;

namespace Codist.QuickInfo
{
	sealed class QuickInfoVisibilityController : IAsyncQuickInfoSource
	{
		public async Task<QuickInfoItem> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken) {
			await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
			// hide Quick Info when:
			//   CtrlQuickInfo option is on and shift is not pressed,
			//   or CtrlQuickInfo is off and shift is pressed
			if (Config.Instance.QuickInfoOptions.MatchFlags(QuickInfoOptions.CtrlQuickInfo)
				^ Keyboard.Modifiers.MatchFlags(ModifierKeys.Shift)
				// do not show Quick Info when user is hovering on the SmartBar or the SymbolList
				|| session.TextView.Properties.ContainsProperty(SmartBars.SmartBar.QuickInfoSuppressionId)
				|| session.TextView.Properties.ContainsProperty(Controls.ExternalAdornment.QuickInfoSuppressionId)
				) {
				await session.DismissAsync();
			}
			return null;
		}

		void IDisposable.Dispose() { }
	}
}
