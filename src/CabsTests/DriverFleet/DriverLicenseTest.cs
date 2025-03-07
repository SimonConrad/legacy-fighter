﻿using System;
using LegacyFighter.Cabs.DriverFleet;

namespace LegacyFighter.CabsTests.DriverFleet;

public class DriverLicenseTest
{
  [Test]
  public void CannotCreateInvalidLicense()
  {
    new Func<DriverLicense>(() => DriverLicense.WithLicense("invalid"))
      .Should().ThrowExactly<ArgumentException>();
    new Func<DriverLicense>(() => DriverLicense.WithLicense(string.Empty))
      .Should().ThrowExactly<ArgumentException>();
  }

  [Test]
  public void CanCreateValidLicense()
  {
    //when
    var license = DriverLicense.WithLicense("FARME100165AB5EW");

    //then
    Assert.AreEqual("FARME100165AB5EW", license.ValueAsString);
  }

  [Test]
  public void CanCreateInvalidLicenseExplicitly()
  {
    //when
    var license = DriverLicense.WithoutValidation("invalid");

    //then
    Assert.AreEqual("invalid", license.ValueAsString);
  }

}