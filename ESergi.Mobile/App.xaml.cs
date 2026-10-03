using Microsoft.Extensions.DependencyInjection;

namespace ESergi.Mobile;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		ThemePalette.Initialize(this);
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}
