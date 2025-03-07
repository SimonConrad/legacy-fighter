﻿using LegacyFighter.Cabs.Config;
using LegacyFighter.CabsTests.Common;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using System.Collections.Generic;
using System.Linq;
using LegacyFighter.Cabs.Crm;
using LegacyFighter.Cabs.Geolocation;
using LegacyFighter.Cabs.Loyalty;

namespace LegacyFighter.CabsTests.Integration;

public class RemovingAwardMilesIntegrationTest
{
  private const long TransitId = 1L;
  private static readonly Instant DayBeforeYesterday = new LocalDateTime(1989, 12, 12, 12, 12).InUtc().ToInstant();
  private static readonly Instant Yesterday = DayBeforeYesterday.Plus(Duration.FromDays(1));
  private static readonly Instant Today = Yesterday.Plus(Duration.FromDays(1));
  private static readonly Instant Sunday = new LocalDateTime(1989, 12, 17, 12, 12).InUtc().ToInstant();

  private CabsApp _app = default!;
  private Fixtures Fixtures => _app.Fixtures;
  private IAwardsService AwardsService => _app.AwardsService;
  private IAwardsAccountRepository AwardsAccountRepository => _app.AwardsAccountRepository;
  private IClock Clock { get; set; } = default!;
  private IGeocodingService GeocodingService { get; set; } = default!;
  private IAppProperties AppProperties { get; set; } = default!;

  [SetUp]
  public void InitializeApp()
  {
    AppProperties = Substitute.For<IAppProperties>();
    Clock = Substitute.For<IClock>();
    GeocodingService = Substitute.For<IGeocodingService>();
    _app = CabsApp.CreateInstance(collection =>
    {
      collection.AddSingleton(Clock);
      collection.AddSingleton(GeocodingService);
      collection.AddSingleton(AppProperties);
    });
  }

  [TearDown]
  public async Task DisposeOfApp()
  {
    await _app.DisposeAsync();
  }

  [Test]
  public async Task ByDefaultRemoveOldestFirstEvenWhenTheyAreNonExpiring()
  {
    //given
    var client = await ClientWithAnActiveMilesProgram(Client.Types.Normal);
    //and
    var middle = await GrantedMilesThatWillExpireInDays(10, 365, Yesterday, client);
    var youngest = await GrantedMilesThatWillExpireInDays(10, 365, Today, client);
    var oldestNonExpiringMiles = await GrantedNonExpiringMiles(5, DayBeforeYesterday, client);

    //when
    await AwardsService.RemoveMiles(client.Id, 16);

    //then
    var awardedMiles = await AwardsAccountRepository.FindAllMilesBy(client);
    AssertThatMilesWereReducedTo(oldestNonExpiringMiles, 0, awardedMiles);
    AssertThatMilesWereReducedTo(middle, 0, awardedMiles);
    AssertThatMilesWereReducedTo(youngest, 9, awardedMiles);
  }

  [Test]
  public async Task ShouldRemoveOldestMilesFirstWhenManyTransits()
  {
    //given
    var client = await ClientWithAnActiveMilesProgram(Client.Types.Normal);
    //and
    await Fixtures.ClientHasDoneTransits(client, 15, GeocodingService);
    //and
    var oldest = await GrantedMilesThatWillExpireInDays(10, 60, DayBeforeYesterday, client);
    var middle = await GrantedMilesThatWillExpireInDays(10, 365, Yesterday, client);
    var youngest = await GrantedMilesThatWillExpireInDays(10, 30, Today, client);

    //when
    await AwardsService.RemoveMiles(client.Id, 15);

    //then
    var awardedMiles = await AwardsAccountRepository.FindAllMilesBy(client);
    AssertThatMilesWereReducedTo(oldest, 0, awardedMiles);
    AssertThatMilesWereReducedTo(middle, 5, awardedMiles);
    AssertThatMilesWereReducedTo(youngest, 10, awardedMiles);
  }

  [Test]
  public async Task ShouldRemoveNonExpiringMilesLastWhenManyTransits()
  {
    //given
    var client = await ClientWithAnActiveMilesProgram(Client.Types.Normal);
    //and
    await Fixtures.ClientHasDoneTransits(client, 15, GeocodingService);

    var regularMiles = await GrantedMilesThatWillExpireInDays(10, 365, Today, client);
    var oldestNonExpiringMiles = await GrantedNonExpiringMiles(5, DayBeforeYesterday, client);

    //when
    await AwardsService.RemoveMiles(client.Id, 13);

    //then
    var awardedMiles = await AwardsAccountRepository.FindAllMilesBy(client);
    AssertThatMilesWereReducedTo(regularMiles, 0, awardedMiles);
    AssertThatMilesWereReducedTo(oldestNonExpiringMiles, 2, awardedMiles);
  }

  [Test]
  public async Task ShouldRemoveSoonToExpireMilesFirstWhenClientIsVip()
  {
    //given
    var client = await ClientWithAnActiveMilesProgram(Client.Types.Vip);
    //and
    var secondToExpire = await GrantedMilesThatWillExpireInDays(10, 60, Yesterday, client);
    var thirdToExpire = await GrantedMilesThatWillExpireInDays(5, 365, DayBeforeYesterday, client);
    var firstToExpire = await GrantedMilesThatWillExpireInDays(15, 30, Today, client);
    var nonExpiringMiles = await GrantedNonExpiringMiles(1, DayBeforeYesterday, client);

    //when
    await AwardsService.RemoveMiles(client.Id, 21);

    //then
    var awardedMiles = await AwardsAccountRepository.FindAllMilesBy(client);
    AssertThatMilesWereReducedTo(nonExpiringMiles, 1, awardedMiles);
    AssertThatMilesWereReducedTo(firstToExpire, 0, awardedMiles);
    AssertThatMilesWereReducedTo(secondToExpire, 4, awardedMiles);
    AssertThatMilesWereReducedTo(thirdToExpire, 5, awardedMiles);
  }

  [Test]
  public async Task ShouldRemoveSoonToExpireMilesFirstWhenRemovingOnSundayAndClientHasDoneManyTransits()
  {
    //given
    var client = await ClientWithAnActiveMilesProgram(Client.Types.Normal);
    //and
    await Fixtures.ClientHasDoneTransits(client, 15, GeocodingService);
    //and
    var secondToExpire = await GrantedMilesThatWillExpireInDays(10, 60, Yesterday, client);
    var thirdToExpire = await GrantedMilesThatWillExpireInDays(5, 365, DayBeforeYesterday, client);
    var firstToExpire = await GrantedMilesThatWillExpireInDays(15, 10, Today, client);
    var nonExpiringMiles = await GrantedNonExpiringMiles(100, Yesterday, client);

    //when
    ItIsSunday();
    await AwardsService.RemoveMiles(client.Id, 21);

    //then
    var awardedMiles = await AwardsAccountRepository.FindAllMilesBy(client);
    AssertThatMilesWereReducedTo(nonExpiringMiles, 100, awardedMiles);
    AssertThatMilesWereReducedTo(firstToExpire, 0, awardedMiles);
    AssertThatMilesWereReducedTo(secondToExpire, 4, awardedMiles);
    AssertThatMilesWereReducedTo(thirdToExpire, 5, awardedMiles);
  }

  [Test]
  public async Task ShouldRemoveExpiringMilesFirstWhenClientHasManyClaims()
  {
    //given
    var client = await ClientWithAnActiveMilesProgram(Client.Types.Normal);
    //and
    await Fixtures.ClientHasDoneClaimsAfterCompletedTransit(client, 3);
    //and
    var secondToExpire = await GrantedMilesThatWillExpireInDays(4, 60, Yesterday, client);
    var thirdToExpire = await GrantedMilesThatWillExpireInDays(10, 365, DayBeforeYesterday, client);
    var firstToExpire = await GrantedMilesThatWillExpireInDays(5, 10, Yesterday, client);
    var nonExpiringMiles = await GrantedNonExpiringMiles(10, Yesterday, client);

    //when
    await AwardsService.RemoveMiles(client.Id, 21);

    //then
    var awardedMiles = await AwardsAccountRepository.FindAllMilesBy(client);
    AssertThatMilesWereReducedTo(nonExpiringMiles, 0, awardedMiles);
    AssertThatMilesWereReducedTo(thirdToExpire, 0, awardedMiles);
    AssertThatMilesWereReducedTo(secondToExpire, 3, awardedMiles);
    AssertThatMilesWereReducedTo(firstToExpire, 5, awardedMiles);
  }

  private async Task<AwardedMiles> GrantedMilesThatWillExpireInDays(int miles, int expirationInDays, Instant when,
    Client client)
  {
    MilesWillExpireInDays(expirationInDays);
    DefaultMilesBonusIs(miles);
    return await MilesRegisteredAt(when, client);
  }

  private async Task<AwardedMiles> GrantedNonExpiringMiles(int miles, Instant when, Client client)
  {
    DefaultMilesBonusIs(miles);
    Clock.GetCurrentInstant().Returns(when);
    return await AwardsService.RegisterNonExpiringMiles(client.Id, miles);
  }

  private void AssertThatMilesWereReducedTo(AwardedMiles firstToExpire, int milesAfterReduction,
    IReadOnlyList<AwardedMiles> allMiles)
  {
    var actual = allMiles
      .Where(
        am => firstToExpire.Date == am.Date &&
              firstToExpire.ExpirationDate == am.ExpirationDate
      ).Select(am => am.Miles.GetAmountFor(Instant.MinValue));
    actual.First().Should().Be(milesAfterReduction);
  }

  private async Task<AwardedMiles> MilesRegisteredAt(Instant when, Client client)
  {
    Clock.GetCurrentInstant().Returns(when);
    return await AwardsService.RegisterMiles(client.Id, TransitId);
  }

  private async Task<Client> ClientWithAnActiveMilesProgram(Client.Types type)
  {
    Clock.GetCurrentInstant().Returns(DayBeforeYesterday);
    var client = await Fixtures.AClient(type);
    await Fixtures.ActiveAwardsAccount(client);
    return client;
  }

  private void MilesWillExpireInDays(int days)
  {
    AppProperties.MilesExpirationInDays.Returns(days);
  }

  private void DefaultMilesBonusIs(int miles)
  {
    AppProperties.DefaultMilesBonus.Returns(miles);
  }

  private void ItIsSunday()
  {
    Clock.GetCurrentInstant().Returns(Sunday);
  }
}