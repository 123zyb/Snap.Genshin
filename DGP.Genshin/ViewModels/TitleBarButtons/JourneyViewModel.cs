﻿using DGP.Genshin.Controls.TitleBarButtons;
using DGP.Genshin.Helpers;
using DGP.Genshin.Messages;
using DGP.Genshin.MiHoYoAPI.GameRole;
using DGP.Genshin.MiHoYoAPI.Journey;
using DGP.Genshin.Services.Abstratcions;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.Input;
using Microsoft.Toolkit.Mvvm.Messaging;
using ModernWpf.Controls.Primitives;
using Snap.Core.DependencyInjection;
using Snap.Extenion.Enumerable;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace DGP.Genshin.ViewModels.TitleBarButtons
{
    /// <summary>
    /// 旅行札记服务
    /// </summary>
    [ViewModel(InjectAs.Transient)]
    public class JourneyViewModel : ObservableRecipient, IRecipient<CookieChangedMessage>
    {
        private readonly ICookieService cookieService;

        private JourneyProvider journeyProvider;
        private UserGameRoleProvider userGameRoleProvider;

        private JourneyInfo? journeyInfo;
        private List<UserGameRole> userGameRoles = new();
        private UserGameRole? selectedRole;
        private IAsyncRelayCommand<TitleBarButton> openUICommand;

        public JourneyInfo? JourneyInfo { get => journeyInfo; set => SetProperty(ref journeyInfo, value); }
        public List<UserGameRole> UserGameRoles { get => userGameRoles; set => SetProperty(ref userGameRoles, value); }
        public UserGameRole? SelectedRole
        {
            get => selectedRole; set
            {
                SetProperty(ref selectedRole, value);
                UpdateJourneyInfo(value);
            }
        }
        private async void UpdateJourneyInfo(UserGameRole? role)
        {
            JourneyInfo = await journeyProvider.GetMonthInfoAsync(role?.GameUid, role?.Region);
        }
        public IAsyncRelayCommand<TitleBarButton> OpenUICommand
        {
            get => openUICommand;

            [MemberNotNull(nameof(openUICommand))]
            set => SetProperty(ref openUICommand, value);
        }

        public JourneyViewModel(ICookieService cookieService, IMessenger messenger) : base(messenger)
        {
            this.cookieService = cookieService;

            journeyProvider = new JourneyProvider(this.cookieService.CurrentCookie);
            userGameRoleProvider = new UserGameRoleProvider(this.cookieService.CurrentCookie);

            OpenUICommand = new AsyncRelayCommand<TitleBarButton>(OpenUIAsync);

            IsActive = true;
        }

        ~JourneyViewModel()
        {
            IsActive = false;
        }

        private async Task OpenUIAsync(TitleBarButton? t)
        {
            if (t?.ShowAttachedFlyout<Grid>(this) == true)
            {
                new Event(t.GetType(), true).TrackAs(Event.OpenTitle);
                await InitializeInternalAsync();
            }
        }
        private async Task InitializeInternalAsync()
        {
            UserGameRoles = await userGameRoleProvider.GetUserGameRolesAsync();
            SelectedRole = UserGameRoles.MatchedOrFirst(i => i.IsChosen);
        }

        public void Receive(CookieChangedMessage message)
        {
            journeyProvider = new JourneyProvider(message.Value);
            userGameRoleProvider = new UserGameRoleProvider(message.Value);
        }
    }
}