using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using WalletWasabi.Fluent.ViewModels.NavBar;
using WalletWasabi.Fluent.ViewModels.Search;

namespace WalletWasabi.Fluent.ViewModels
{
	public class SearchPageViewModel : NavBarItemViewModel
	{
		private string? _searchQuery;
		private readonly ReadOnlyObservableCollection<SearchItemViewModel> _searchItems;

		public SearchPageViewModel(NavigationStateViewModel navigationState, WalletManagerViewModel walletManager, AddWalletPageViewModel addWalletPage) : base(navigationState, NavigationTarget.Home)
		{
			Title = "Search";

			var searchItems = new SourceList<SearchItemViewModel>();

			searchItems.Add(new SearchItemViewModel(
				navigationState,
				NavigationTarget.HomeScreen,
				iconName: "home_regular",
				title: "Home",
				category: "General",
				keywords: "Home",
				() => new HomePageViewModel(navigationState, walletManager, addWalletPage)));

			searchItems.Add(new SearchItemViewModel(
				navigationState,
				NavigationTarget.HomeScreen,
				iconName: "settings_regular",
				title: "Settings",
				category: "General",
				keywords: "Settings, General, User Interface, Privacy, Advanced",
				() => new SettingsPageViewModel(navigationState)));

			searchItems.Add(new SearchItemViewModel(
				navigationState,
				NavigationTarget.DialogScreen,
				iconName: "add_circle_regular",
				title: "Add Wallet",
				category: "Wallet",
				keywords: "Wallet, Add Wallet, Create Wallet, Recover Wallet, Import Wallet, Connect Hardware Wallet",
				() => addWalletPage));

			walletManager.Items.ToObservableChangeSet()
				.Cast(x => new SearchItemViewModel(
					navigationState,
					NavigationTarget.HomeScreen,
					iconName: "web_asset_regular",
					title: x.WalletName,
					category: "Wallet",
					keywords: $"Wallet, {x.WalletName}",
					() => x))
				.Sort(SortExpressionComparer<SearchItemViewModel>.Ascending(i => i.Title))
				.Merge(searchItems.Connect())
				.ObserveOn(RxApp.MainThreadScheduler)
				.Bind(out _searchItems)
				.AsObservableList();
		}

		public override string IconName => "search_regular";

		public string? SearchQuery
		{
			get => _searchQuery;
			set => this.RaiseAndSetIfChanged(ref _searchQuery, value);
		}

		public ReadOnlyObservableCollection<SearchItemViewModel> SearchItems => _searchItems;
	}
}