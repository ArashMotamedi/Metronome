using Metronome.Resources;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using Microsoft.Phone.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Metronome
{

	class MetronomeTick : DependencyObject
	{
		public SoundEffect TickSound;

		public static readonly DependencyProperty TickProperty =
			DependencyProperty.Register("Tick", typeof(double), typeof(MetronomeTick),
			new PropertyMetadata(new PropertyChangedCallback((dpo, dpoe) => { if ((double)dpoe.NewValue != 0) ((MetronomeTick)dpo).TickSound.Play(); })));

		public MetronomeTick()
		{
			Stream tickStream = TitleContainer.OpenStream("Assets/tick.wav");
			TickSound = SoundEffect.FromStream(tickStream);
			FrameworkDispatcher.Update();
		}

		public double Tick
		{
			get { return (double)GetValue(TickProperty); }
			set { SetValue(TickProperty, value); }
		}
	}

	struct Range
	{
		public readonly int Min, Max;
		public readonly double Center;
		public readonly double Length;
		public Range(int min, int max)
		{
			Min = min;
			Max = max;
			Center = (min + max) / 2;
			Length = Math.Abs(min - max);
		}
	}

	public partial class MetronomePage : PhoneApplicationPage
	{
		Dictionary<Grid, Range> TempoRanges;
		DispatcherTimer demoTimer;
		MetronomeTick metronomeTick = new MetronomeTick();
		DateTime lastDemoDelayRequested = DateTime.Now;

		int tempoRangeLow = 42;
		int tempoRangeHigh = 208;
		int[] tempos = { 40, 42, 44, 46, 48, 50, 52, 54, 56, 58, 60, 63, 66, 69, 72, 76, 80, 84, 88, 92, 96, 
						   100, 104, 108, 112, 116, 120, 126, 132, 138, 144, 152, 160, 168, 176, 184, 192, 200, 208 };

		Grid[] rangeGrids;
		bool demoCompleted = false;

		int weightRangeLow = 10;
		int weightRangeHigh = 580;
		int currentTempo;

		RotateTransform barTransform;
		TranslateTransform weightTransform;
		TranslateTransform metronomeTransform;
		TranslateTransform controlPanelTransform;
		CompositeTransform demoTransform;
		Storyboard tickingAnimation;
		Storyboard interimTransitionAnimation = new Storyboard();
		Storyboard metronomeTransitionAnimation = new Storyboard();
		int interimTransitionDirection;
		bool commitToMetronome = false;

		Duration transitionDuration = TimeSpan.FromMilliseconds(600);
		EasingFunctionBase transitionEase = new ExponentialEase
		{
			Exponent = 5,
			EasingMode = EasingMode.EaseOut
		};

		double weightStartOffset;
		enum AppState { ControlPanel, Ticking, Transitioning }
		AppState appState = AppState.Transitioning;

		public MetronomePage()
		{
			InitializeComponent();
			((ApplicationBarMenuItem)(ApplicationBar.MenuItems[0])).Text = AppResources.RateAndReview;

			// if demo is needed
			demoCompleted = System.IO.IsolatedStorage.IsolatedStorageFile.GetUserStoreForApplication().FileExists("DemoCompleted");
			if (!demoCompleted)
			{
				demoTimer = new DispatcherTimer();
				demoTimer.Interval = TimeSpan.FromMilliseconds(100);
				demoTimer.Tick += (o, s) =>
				{
					// Delay
					if (DateTime.Now.Subtract(lastDemoDelayRequested).TotalMilliseconds < demoTimer.Interval.TotalMilliseconds)
						return;

					// Stop
					if (demoCompleted)
					{
						demoTimer.Stop();
						return;
					}

					// Run demo
					if (appState == AppState.ControlPanel)
					{
						demoTimer.Interval = TimeSpan.FromMilliseconds(3000);
						DemoSwipe(true);
					}
					else if (appState == AppState.Ticking)
					{
						demoTimer.Interval = TimeSpan.FromMilliseconds(5000);
						DemoSwipe(false);
					}
				};
				demoTimer.Start();
			}

			TempoRanges = new Dictionary<Grid, Range>();
			TempoRanges.Add(rangeLargo, new Range(40, 60));
			TempoRanges.Add(rangeLarghetto, new Range(60, 66));
			TempoRanges.Add(rangeAdagio, new Range(66, 76));
			TempoRanges.Add(rangeAndante, new Range(76, 108));
			TempoRanges.Add(rangeModerato, new Range(108, 120));
			TempoRanges.Add(rangeAllegro, new Range(120, 168));
			TempoRanges.Add(rangePresto, new Range(168, 200));
			TempoRanges.Add(rangePrestissimo, new Range(200, 208));

			// Clickable ranges
			foreach (var tempoRange in TempoRanges)
			{
				var p = new PlaneProjection();
				tempoRange.Key.Projection = p;
				p.CenterOfRotationX = 50;
				p.CenterOfRotationY = 0;
				p.CenterOfRotationZ = 0;
				tempoRange.Key.Tap += (o, s) =>
				{
					p.RotationX = 0;
					p.RotationY = 0.2;
					Storyboard sb = new Storyboard();
					DoubleAnimation da1 = new DoubleAnimation();
					DoubleAnimation da2 = new DoubleAnimation();
					da1.To = 0;
					da2.To = 0;
					sb.Duration = da1.Duration = da2.Duration = TimeSpan.FromMilliseconds(350);
					da1.EasingFunction = da2.EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
					sb.Children.Add(da1);
					sb.Children.Add(da2);
					Storyboard.SetTarget(da1, p);
					Storyboard.SetTarget(da2, p);
					Storyboard.SetTargetProperty(da1, new PropertyPath(PlaneProjection.RotationXProperty));
					Storyboard.SetTargetProperty(da2, new PropertyPath(PlaneProjection.RotationYProperty));
					sb.Begin();
					SetTempo(tempoRange.Value.Center + 0.1, true);
				};
			}

			barTransform = new RotateTransform();
			weightTransform = new TranslateTransform();
			metronomeTransform = new TranslateTransform();
			controlPanelTransform = new TranslateTransform();
			demoTransform = new CompositeTransform();

			MetronomeBar.RenderTransform = barTransform;
			WeightImage.RenderTransform = weightTransform;
			MetronomeGrid.RenderTransform = metronomeTransform;
			ControlPanelGrid.RenderTransform = controlPanelTransform;
			DemoFinger.RenderTransform = demoTransform;

			rangeGrids = new Grid[] { rangeLargo, rangeLarghetto, rangeAdagio, rangeAndante, rangeModerato, rangeAllegro, rangePresto, rangePrestissimo };

			barTransform.CenterX = 42;
			barTransform.CenterY = 1000;

			demoTransform.CenterX = 50;
			demoTransform.CenterY = 350;

			metronomeTransform.X = 160;
			ShellBackground.Opacity = 0;
			ShellForeground.Opacity = 0;

			TemposPanel.Opacity = 0;
			txtTempo.Opacity = 0;

			currentTempo = 72;
			try
			{
				if (System.IO.IsolatedStorage.IsolatedStorageFile.GetUserStoreForApplication().FileExists("LastTempo"))
				{
					var fs = System.IO.IsolatedStorage.IsolatedStorageFile.GetUserStoreForApplication().OpenFile("LastTempo", FileMode.Open);
					currentTempo = fs.ReadByte();
					fs.Close();
				}
			}
			catch { }

			SetTempo(currentTempo, true);
			TransitionControlPanel(true, () => { appState = AppState.ControlPanel; });
		}

		protected override void OnBackKeyPress(System.ComponentModel.CancelEventArgs e)
		{
			if (appState == AppState.Ticking)
			{
				e.Cancel = true;
				TransitionMetronome(false, true);
				TransitionControlPanel(true, () =>
				{
					appState = AppState.ControlPanel;
				});
			}
		}

		private void TransitionControlPanel(bool intoView, Action a = null)
		{
			appState = AppState.Transitioning;
			DelayDemo();

			PhoneApplicationService.Current.UserIdleDetectionMode = intoView ? IdleDetectionMode.Enabled : IdleDetectionMode.Disabled;
			ApplicationBar.IsVisible = intoView;

			Storyboard sb = new Storyboard();
			DoubleAnimation daTranslate = new DoubleAnimation();
			DoubleAnimation daOpacity = new DoubleAnimation();
			DoubleAnimation daTempoOpacity = new DoubleAnimation();
			daTranslate.To = intoView ? 20 : 0;
			daOpacity.To = intoView ? 1 : 0;
			daTempoOpacity.To = intoView ? 1 : 0.5;

			daTranslate.EasingFunction =
				daOpacity.EasingFunction =
				daTempoOpacity.EasingFunction = transitionEase;

			sb.Duration =
				daTranslate.Duration =
				daTempoOpacity.Duration = transitionDuration;

			daOpacity.Duration = intoView ? transitionDuration : TimeSpan.FromMilliseconds(100);

			Storyboard.SetTarget(daTranslate, controlPanelTransform);
			Storyboard.SetTarget(daOpacity, TemposPanel);
			Storyboard.SetTarget(daTempoOpacity, txtTempo);

			Storyboard.SetTargetProperty(daTranslate, new PropertyPath(TranslateTransform.XProperty));
			Storyboard.SetTargetProperty(daOpacity, new PropertyPath(UIElement.OpacityProperty));
			Storyboard.SetTargetProperty(daTempoOpacity, new PropertyPath(UIElement.OpacityProperty));

			sb.Children.Add(daTranslate);
			sb.Children.Add(daOpacity);
			sb.Children.Add(daTempoOpacity);

			if (a != null)
				sb.Completed += (o, s) => { a(); };

			sb.Begin();
		}

		private void TransitionMetronome(bool intoView, bool setTransitioningState, Action a = null)
		{
			if (setTransitioningState) appState = AppState.Transitioning;
			DelayDemo();

			if (tickingAnimation != null)
			{
				tickingAnimation.Pause();
			}

			if (!demoCompleted)
			{
				if (intoView)
				{
					demoTimer.Interval = TimeSpan.FromMilliseconds(1000);
				}
				else
				{
					demoCompleted = true;
					System.IO.IsolatedStorage.IsolatedStorageFile.GetUserStoreForApplication().OpenFile("DemoCompleted", FileMode.OpenOrCreate).Close();
				}
			}

			if (intoView)
			{
				metronomeTick.TickSound.Play(0, 0, 0);
			}

			metronomeTransitionAnimation = new Storyboard();
			DoubleAnimation daTranslate = new DoubleAnimation();
			DoubleAnimation daBackShellOpacity = new DoubleAnimation();
			DoubleAnimation daFrontShellOpacity = new DoubleAnimation();
			DoubleAnimation daAngle = new DoubleAnimation();

			daTranslate.To = intoView ? 0 : 160;
			daBackShellOpacity.To = intoView ? 1 : 0;
			daFrontShellOpacity.To = intoView ? 1 : 0;
			daAngle.To = intoView ? TempoAngle() : 0;

			daTranslate.EasingFunction =
				daBackShellOpacity.EasingFunction =
				daFrontShellOpacity.EasingFunction = transitionEase;

			metronomeTransitionAnimation.Duration =
				daTranslate.Duration =
				daBackShellOpacity.Duration =
				daFrontShellOpacity.Duration = transitionDuration;

			daAngle.Duration = TimeSpan.FromMilliseconds(cap(TempoTime().TotalMilliseconds / 2 - 200, 300, transitionDuration.TimeSpan.TotalMilliseconds - 100));
			daAngle.EasingFunction = new PowerEase
			{
				Power = 3,
				EasingMode = EasingMode.EaseOut
			};

			if (intoView)
				daAngle.Completed += (o, s) => { StartMetronomeAnimation(); };

			Storyboard.SetTarget(daTranslate, metronomeTransform);
			Storyboard.SetTarget(daBackShellOpacity, ShellBackground);
			Storyboard.SetTarget(daFrontShellOpacity, ShellForeground);
			Storyboard.SetTarget(daAngle, barTransform);

			Storyboard.SetTargetProperty(daTranslate, new PropertyPath(TranslateTransform.XProperty));
			Storyboard.SetTargetProperty(daBackShellOpacity, new PropertyPath(UIElement.OpacityProperty));
			Storyboard.SetTargetProperty(daFrontShellOpacity, new PropertyPath(UIElement.OpacityProperty));
			Storyboard.SetTargetProperty(daAngle, new PropertyPath(RotateTransform.AngleProperty));

			metronomeTransitionAnimation.Children.Add(daTranslate);
			metronomeTransitionAnimation.Children.Add(daBackShellOpacity);
			metronomeTransitionAnimation.Children.Add(daFrontShellOpacity);
			metronomeTransitionAnimation.Children.Add(daAngle);

			if (a != null)
				metronomeTransitionAnimation.Completed += (o, s) => { a(); };

			metronomeTransitionAnimation.Begin();
		}

		private void DemoSwipe(bool toStartTicking)
		{
			Storyboard sb = new Storyboard();
			DoubleAnimation daOpacity = new DoubleAnimation();
			DoubleAnimation daAngle = new DoubleAnimation();

			daOpacity.To = 0;
			daOpacity.From = 1;

			daAngle.From = toStartTicking ? 20 : -20;
			daAngle.To = toStartTicking ? -20 : 20;

			daOpacity.EasingFunction = new ExponentialEase { Exponent = 5, EasingMode = EasingMode.EaseIn };
			daAngle.EasingFunction = new ExponentialEase { Exponent = 7, EasingMode = EasingMode.EaseInOut };

			sb.Duration =
				daOpacity.Duration =
				daAngle.Duration = TimeSpan.FromMilliseconds(1500);

			Storyboard.SetTarget(daOpacity, DemoFinger);
			Storyboard.SetTarget(daAngle, demoTransform);

			Storyboard.SetTargetProperty(daOpacity, new PropertyPath(UIElement.OpacityProperty));
			Storyboard.SetTargetProperty(daAngle, new PropertyPath(CompositeTransform.RotationProperty));

			sb.Children.Add(daOpacity);
			sb.Children.Add(daAngle);

			sb.Begin();
		}

		private void SetTempo(double tempo, bool setWeightPosition)
		{
			int i = FindTempoIndex(tempo);
			currentTempo = tempos[i];
			txtTempo.Text = currentTempo.ToString();

			foreach (var item in TempoRanges)
			{
				item.Key.Opacity =
					(currentTempo >= item.Value.Min && currentTempo <= item.Value.Max) ?
					1 : 0.5;
			}


			if (setWeightPosition)
				weightTransform.Y = weightRangeLow + (((i) / (double)(tempos.Length - 1)) * (weightRangeHigh - weightRangeLow));
		}

		private int FindTempoIndex(double tempo)
		{
			int t = tempos.OrderBy(n => Math.Abs(n - tempo)).First();
			int i;
			for (i = 0; i < tempos.Length; i++)
			{
				if (tempos[i] == t)
					break;
			}
			return i;
		}

		private void StartMetronomeAnimation()
		{
			if (tickingAnimation != null)
			{
				tickingAnimation.Pause();
			}

			tickingAnimation = new Storyboard();
			metronomeTick.Tick = 0;

			// Durations
			var duration = TempoTime();
			var halftime = TimeSpan.FromMilliseconds(duration.TotalMilliseconds / 2d);
			var quartertime = TimeSpan.FromMilliseconds(duration.TotalMilliseconds / 4d);
			var threequarterstime = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 3 / 4d);

			// Easing
			var ease = new PowerEase();
			ease.EasingMode = EasingMode.EaseInOut;
			ease.Power = 3;

			// Animation and keyframes
			var da = new DoubleAnimationUsingKeyFrames();
			var kf0 = new DiscreteDoubleKeyFrame();
			var kf1 = new EasingDoubleKeyFrame();
			var kf2 = new EasingDoubleKeyFrame();

			var daTick = new DoubleAnimationUsingKeyFrames();
			var kft0 = new DiscreteDoubleKeyFrame();
			var kft1 = new DiscreteDoubleKeyFrame();

			// Ease
			kf1.EasingFunction = ease;
			kf2.EasingFunction = ease;

			// Times
			kf0.KeyTime = TimeSpan.FromMilliseconds(0);
			kf1.KeyTime = halftime;
			kf2.KeyTime = duration;

			kft0.KeyTime = quartertime;
			kft1.KeyTime = threequarterstime;

			int angle = TempoAngle();

			// Values
			kf0.Value = angle;
			kf1.Value = -angle;
			kf2.Value = angle;

			kft0.Value = 1;
			kft1.Value = 2;

			// Settings
			tickingAnimation.RepeatBehavior = RepeatBehavior.Forever;
			tickingAnimation.Duration = duration;
			da.Duration = duration;
			daTick.Duration = duration;

			// Linking
			da.KeyFrames.Add(kf0);
			da.KeyFrames.Add(kf1);
			da.KeyFrames.Add(kf2);

			daTick.KeyFrames.Add(kft0);
			daTick.KeyFrames.Add(kft1);

			tickingAnimation.Children.Add(da);
			tickingAnimation.Children.Add(daTick);

			Storyboard.SetTarget(da, barTransform);
			Storyboard.SetTargetProperty(da, new PropertyPath(RotateTransform.AngleProperty));

			Storyboard.SetTarget(daTick, metronomeTick);
			Storyboard.SetTargetProperty(daTick, new PropertyPath(MetronomeTick.TickProperty));

			// Tick sound
			tickingAnimation.Begin();
		}

		private TimeSpan TempoTime()
		{
			return TimeSpan.FromMilliseconds(120000 / currentTempo);
		}

		private int TempoAngle()
		{
			return 17 - (int)(5 * ((double)(currentTempo - tempoRangeLow) / (tempoRangeHigh - tempoRangeLow)));
		}

		private void WeightImage_ManipulationDelta(object sender, System.Windows.Input.ManipulationDeltaEventArgs e)
		{
			e.Handled = true;
			DelayDemo();
			if (appState == AppState.ControlPanel)
			{
				double y = cap(weightStartOffset + e.CumulativeManipulation.Translation.Y, weightRangeLow, weightRangeHigh);
				weightTransform.Y = y;
				int i = (int)((y - weightRangeLow) / (weightRangeHigh - weightRangeLow) * (tempos.Length - 1));
				SetTempo(tempos[i], false);
			}
		}

		private double cap(double value, double min, double max)
		{
			return Math.Max(min, Math.Min(max, value));
		}

		private void WeightImage_ManipulationStarted(object sender, System.Windows.Input.ManipulationStartedEventArgs e)
		{
			e.Handled = true;
			DelayDemo();
			weightStartOffset = weightTransform.Y;
			commitToMetronome = false;
		}

		private void LayoutRoot_ManipulationStarted(object sender, System.Windows.Input.ManipulationStartedEventArgs e)
		{
			e.Handled = true;
			DelayDemo();
			interimTransitionDirection = 1;
			commitToMetronome = false;
		}

		private void LayoutRoot_ManipulationDelta(object sender, System.Windows.Input.ManipulationDeltaEventArgs e)
		{
			e.Handled = true;
			DelayDemo();
			if (appState != AppState.ControlPanel)
			{
				return;
			}

			int newDirection = Math.Sign(e.DeltaManipulation.Translation.X);
			commitToMetronome = newDirection < 0;
			if (newDirection * interimTransitionDirection < 0)
			{
				metronomeTransitionAnimation.Pause();
				interimTransitionDirection = newDirection;
				interimTransitionAnimation = new Storyboard();
				DoubleAnimation da = new DoubleAnimation();
				da.To = (newDirection < 0) ? 0.4 : 0;
				da.Duration = interimTransitionAnimation.Duration = TimeSpan.FromMilliseconds(300);
				interimTransitionAnimation.Children.Add(da);
				Storyboard.SetTarget(da, ShellBackground);
				Storyboard.SetTargetProperty(da, new PropertyPath(OpacityProperty));
				interimTransitionAnimation.Begin();
			}
		}

		private void LayoutRoot_ManipulationCompleted(object sender, System.Windows.Input.ManipulationCompletedEventArgs e)
		{
			e.Handled = true;
			interimTransitionAnimation.Pause();

			if (appState == AppState.ControlPanel)
			{
				if (commitToMetronome)
				{
					commitToMetronome = false;
					TransitionControlPanel(false);
					TransitionMetronome(true, true, () =>
					{
						appState = AppState.Ticking;
					});
				}
				else
				{
					TransitionMetronome(false, false);
				}
			}
			else if (appState == AppState.Ticking && (e.IsInertial && e.FinalVelocities.LinearVelocity.X > 0) || (e.TotalManipulation.Translation.X > 0))
			{
				TransitionMetronome(false, true);
				TransitionControlPanel(true, () =>
				{
					appState = AppState.ControlPanel;
				});
			}
		}

		protected override void OnNavigatingFrom(System.Windows.Navigation.NavigatingCancelEventArgs e)
		{
			try
			{
				var fs = System.IO.IsolatedStorage.IsolatedStorageFile.GetUserStoreForApplication().OpenFile("LastTempo", FileMode.Create);
				fs.Write(new byte[] { Convert.ToByte(currentTempo) }, 0, 1);
				fs.Close();
			}
			catch { }
			base.OnNavigatingFrom(e);
		}

		protected override void OnNavigatedTo(System.Windows.Navigation.NavigationEventArgs e)
		{
			var c = (System.Windows.Media.Color)Resources["PhoneAccentColor"];
			var b = new SolidColorBrush(c);
			double denom = 1.2;
			var cDark = System.Windows.Media.Color.FromArgb(180, (byte)(c.R / denom), (byte)(c.G / denom), (byte)(c.B / denom));
			var bDark = new SolidColorBrush(cDark);
			txtTempo.Foreground = b;
			foreach (var item in TempoRanges)
			{
				//((TextBlock)item.Key.Children[0]).Foreground = bDark;
				((TextBlock)item.Key.Children[1]).Foreground = bDark;
			}
			base.OnNavigatedTo(e);
		}

		private void ApplicationBarMenuItem_Click(object sender, EventArgs e)
		{
			new MarketplaceReviewTask().Show();
		}

		private void DelayDemo()
		{
			lastDemoDelayRequested = DateTime.Now;
		}
	}
}