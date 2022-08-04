﻿using System;
using System.Windows;
using System.Windows.Media;
using AppHelpers;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;

namespace Codist.Margins
{
	sealed class LineNumberMargin : MarginElementBase, IDisposable, IWpfTextViewMargin
	{
		static readonly SolidColorBrush LineNumberBrush = Brushes.DarkGray;
		static readonly Pen LineNumberPen = new Pen(LineNumberBrush, 1) { DashStyle = DashStyles.Dash };
		const double LineNumberRenderPadding = -3;

		IWpfTextView _TextView;
		IEditorFormatMap _EditorFormatMap;
		IVerticalScrollBar _ScrollBar;
		double _ScrollbarWidth;
		bool _Disposed;

		public LineNumberMargin(IWpfTextView textView, IVerticalScrollBar scrollBar) {
			_TextView = textView;
			_ScrollBar = scrollBar;
			_EditorFormatMap = ServicesHelper.Instance.EditorFormatMap.GetEditorFormatMap(textView);

			Visibility = Config.Instance.MarkerOptions.MatchFlags(MarkerOptions.LineNumber) ? Visibility.Visible : Visibility.Collapsed;

			Config.RegisterUpdateHandler(UpdateLineNumberMarginConfig);
			Setup();
			_TextView.Closed += TextView_Closed;
		}

		public override string MarginName => nameof(LineNumberMargin);
		public override double MarginSize => 0;

		void UpdateLineNumberMarginConfig(ConfigUpdatedEventArgs e) {
			if (e.UpdatedFeature.MatchFlags(Features.ScrollbarMarkers) == false) {
				return;
			}
			var setVisible = Config.Instance.MarkerOptions.MatchFlags(MarkerOptions.LineNumber);
			var visible = Visibility == Visibility.Visible;
			if (setVisible == false && visible) {
				Visibility = Visibility.Collapsed;
				_TextView.TextBuffer.Changed -= TextView_TextBufferChanged;
				_ScrollBar.TrackSpanChanged -= OnMappingChanged;
				InvalidateVisual();
			}
			else if (setVisible && visible == false) {
				Visibility = Visibility.Visible;
				Setup();
				InvalidateVisual();
			}
		}

		void Setup() {
			_TextView.TextBuffer.Changed += TextView_TextBufferChanged;
			_ScrollBar.TrackSpanChanged += OnMappingChanged;
		}

		void TextView_TextBufferChanged(object sender, TextContentChangedEventArgs args) {
			if (args.Changes.Count == 0) {
				return;
			}
			InvalidateVisual();
		}

		/// <summary>
		/// Handler for the scrollbar changing its coordinate mapping.
		/// </summary>
		void OnMappingChanged(object sender, EventArgs e) {
			InvalidateVisual();
		}
		/// <summary>
		/// Override for the FrameworkElement's OnRender. When called, redraw all markers.
		/// </summary>
		/// <param name="drawingContext">The <see cref="DrawingContext"/> used to render the margin.</param>
		protected override void OnRender(DrawingContext drawingContext) {
			base.OnRender(drawingContext);
			if (_TextView?.IsClosed != false) {
				return;
			}
			if (Config.Instance.MarkerOptions.MatchFlags(MarkerOptions.LineNumber)) {
				DrawLineNumbers(drawingContext);
			}
		}

		protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) {
			base.OnRenderSizeChanged(sizeInfo);
			var b = _ScrollBar as FrameworkElement;
			_ScrollbarWidth = b.ActualWidth + LineNumberRenderPadding;
			InvalidateVisual();
		}

		void DrawLineNumbers(DrawingContext drawingContext) {
			var snapshot = _TextView.TextSnapshot;
			var lc = snapshot.LineCount;
			var step = lc < 500 ? 50 : lc < 2000 ? 100 : lc < 3000 ? 200 : lc < 5000 ? 500 : lc < 20000 ? 1000 : lc < 100000 ? 5000 : 10000;
			var dy = 0.0;
			for (int i = step; i < lc; i += step) {
				var y = _ScrollBar.GetYCoordinateOfBufferPosition(new SnapshotPoint(snapshot, snapshot.GetLineFromLineNumber(i - 1).Start));
				if (y - dy < 50) {
					continue;
				}
				dy = y;
				drawingContext.DrawLine(LineNumberPen, new Point(-100, y), new Point(100, y));
				var t = WpfHelper.ToFormattedText(i.ToString(), 9, LineNumberBrush);
				drawingContext.DrawText(t, new Point(_ScrollbarWidth - t.Width, y));
			}
		}


		void TextView_Closed(object sender, EventArgs e) {
			Dispose();
		}

		#region IDisposable Support
		void Dispose(bool disposing) {
			if (!_Disposed) {
				if (disposing) {
					Config.UnregisterUpdateHandler(UpdateLineNumberMarginConfig);
					_TextView.TextBuffer.Changed -= TextView_TextBufferChanged;
					_TextView.Closed -= TextView_Closed;
					_ScrollBar.TrackSpanChanged -= OnMappingChanged;
					_TextView = null;
					_ScrollBar = null;
					_EditorFormatMap = null;
				}

				_Disposed = true;
			}
		}

		public override void Dispose() {
			Dispose(true);
		}
		#endregion
	}
}
