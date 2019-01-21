﻿using DataAccess;
using ldapcp;
using Microsoft.SharePoint;
using Microsoft.SharePoint.Administration;
using Microsoft.SharePoint.Administration.Claims;
using Microsoft.SharePoint.WebControls;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Claims;

[SetUpFixture]
public class UnitTestsHelper
{
    public static ldapcp.LDAPCP ClaimsProvider = new ldapcp.LDAPCP(UnitTestsHelper.ClaimsProviderName);
    public const string ClaimsProviderName = "LDAPCP";
    public static string ClaimsProviderConfigName = TestContext.Parameters["ClaimsProviderConfigName"];
    public static Uri Context = new Uri(TestContext.Parameters["TestSiteCollectionUri"]);
    public const int MaxTime = 500000;
    public static string FarmAdmin = TestContext.Parameters["FarmAdmin"];
#if DEBUG
    public const int TestRepeatCount = 5;
#else
    public const int TestRepeatCount = 20;
#endif

    public const string RandomClaimType = "http://schemas.yvand.com/ws/claims/random";
    public const string RandomClaimValue = "IDoNotExist";
    public const string RandomLDAPAttribute = "randomAttribute";
    public const string RandomLDAPClass = "randomClass";

    public static string TrustedGroupToAdd_ClaimType = ClaimsProviderConstants.DefaultMainGroupClaimType;
    public static string TrustedGroupToAdd_ClaimValue = TestContext.Parameters["TrustedGroupToAdd_ClaimValue"];
    public static SPClaim TrustedGroup = new SPClaim(TrustedGroupToAdd_ClaimType, TrustedGroupToAdd_ClaimValue, ClaimValueTypes.String, SPOriginalIssuers.Format(SPOriginalIssuerType.TrustedProvider, SPTrust.Name));

    public static string DataFile_AllAccounts_Search = TestContext.Parameters["DataFile_AllAccounts_Search"];
    public static string DataFile_AllAccounts_Validate = TestContext.Parameters["DataFile_AllAccounts_Validate"];

    public static SPTrustedLoginProvider SPTrust => SPSecurityTokenServiceManager.Local.TrustedLoginProviders.FirstOrDefault(x => String.Equals(x.ClaimProviderName, UnitTestsHelper.ClaimsProviderName, StringComparison.InvariantCultureIgnoreCase));

    static TextWriterTraceListener logFileListener;

    [OneTimeSetUp]
    public static void InitializeSiteCollection()
    {
#if DEBUG
        //return; // Uncommented when debugging LDAPCP code from unit tests
#endif

        logFileListener = new TextWriterTraceListener(TestContext.Parameters["TestLogFileName"]);
        Trace.Listeners.Add(logFileListener);
        Trace.AutoFlush = true;
        Trace.TraceInformation($"{DateTime.Now.ToString("s")} Start integration tests of {ClaimsProviderName} {FileVersionInfo.GetVersionInfo(Assembly.GetAssembly(typeof(ldapcp.LDAPCP)).Location).FileVersion}.");
        Trace.WriteLine($"{DateTime.Now.ToString("s")} DataFile_AllAccounts_Search: {DataFile_AllAccounts_Search}");
        Trace.WriteLine($"{DateTime.Now.ToString("s")} DataFile_AllAccounts_Validate: {DataFile_AllAccounts_Validate}");
        Trace.WriteLine($"{DateTime.Now.ToString("s")} TestSiteCollectionUri: {TestContext.Parameters["TestSiteCollectionUri"]}");
        if (SPTrust == null)
            Trace.TraceError($"{DateTime.Now.ToString("s")} SPTrust: is null");
        else
            Trace.WriteLine($"{DateTime.Now.ToString("s")} SPTrust: {SPTrust.Name}");

        LDAPCPConfig config = LDAPCPConfig.GetConfiguration(UnitTestsHelper.ClaimsProviderConfigName, UnitTestsHelper.SPTrust.Name);
        if (config == null)
        {
            LDAPCPConfig.CreateConfiguration(ClaimsProviderConstants.CONFIG_ID, ClaimsProviderConstants.CONFIG_NAME, SPTrust.Name);
        }

        SPWebApplication wa = SPWebApplication.Lookup(Context);
        if (wa != null)
        {
            Trace.WriteLine($"{DateTime.Now.ToString("s")} Web application {wa.Name} found, checking if site collection {Context.AbsoluteUri} exists...");
            SPClaimProviderManager claimMgr = SPClaimProviderManager.Local;
            string encodedClaim = claimMgr.EncodeClaim(TrustedGroup);
            SPUserInfo userInfo = new SPUserInfo { LoginName = encodedClaim, Name = TrustedGroupToAdd_ClaimValue };

            if (!SPSite.Exists(Context))
            {
                Trace.WriteLine($"{DateTime.Now.ToString("s")} Creating site collection {Context.AbsoluteUri}...");
                SPSite spSite = wa.Sites.Add(Context.AbsoluteUri, ClaimsProviderName, $"DataFile_AllAccounts_Search: {DataFile_AllAccounts_Search}; DataFile_AllAccounts_Validate: {DataFile_AllAccounts_Validate}", 1033, "STS#0", FarmAdmin, String.Empty, String.Empty);
                spSite.RootWeb.CreateDefaultAssociatedGroups(FarmAdmin, FarmAdmin, spSite.RootWeb.Title);

                SPGroup membersGroup = spSite.RootWeb.AssociatedMemberGroup;
                membersGroup.AddUser(userInfo.LoginName, userInfo.Email, userInfo.Name, userInfo.Notes);
                spSite.Dispose();
            }
            else
            {
                using (SPSite spSite = new SPSite(Context.AbsoluteUri))
                {
                    SPGroup membersGroup = spSite.RootWeb.AssociatedMemberGroup;
                    membersGroup.AddUser(userInfo.LoginName, userInfo.Email, userInfo.Name, userInfo.Notes);
                }
            }
        }
        else
        {
            Trace.TraceError($"{DateTime.Now.ToString("s")} Web application {Context} was NOT found.");
        }

    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        Trace.WriteLine($"{DateTime.Now.ToString("s")} Integration tests of {ClaimsProviderName} {FileVersionInfo.GetVersionInfo(Assembly.GetAssembly(typeof(ldapcp.LDAPCP)).Location).FileVersion} finished.");
        Trace.Flush();
        if (logFileListener != null)
            logFileListener.Dispose();
    }

    public static void InitializeConfiguration(LDAPCPConfig config)
    {
        config.ResetCurrentConfiguration();

#if DEBUG
        config.LDAPQueryTimeout = 99999;
#endif

        config.Update();
    }

    /// <summary>
    /// Start search operation on a specific claims provider
    /// </summary>
    /// <param name="inputValue"></param>
    /// <param name="expectedCount">How many entities are expected to be returned. Set to Int32.MaxValue if exact number is unknown but greater than 0</param>
    /// <param name="expectedClaimValue"></param>
    public static void TestSearchOperation(string inputValue, int expectedCount, string expectedClaimValue)
    {
        string[] entityTypes = new string[] { "User", "SecGroup", "SharePointGroup", "System", "FormsRole" };

        SPProviderHierarchyTree providerResults = ClaimsProvider.Search(Context, entityTypes, inputValue, null, 30);
        List<PickerEntity> entities = new List<PickerEntity>();
        foreach (var children in providerResults.Children)
        {
            entities.AddRange(children.EntityData);
        }
        VerifySearchTest(entities, inputValue, expectedCount, expectedClaimValue);

        entities = ClaimsProvider.Resolve(Context, entityTypes, inputValue).ToList();
        VerifySearchTest(entities, inputValue, expectedCount, expectedClaimValue);
    }

    public static void VerifySearchTest(List<PickerEntity> entities, string input, int expectedCount, string expectedClaimValue)
    {
        bool entityValueFound = false;
        foreach (PickerEntity entity in entities)
        {
            if (String.Equals(expectedClaimValue, entity.Claim.Value, StringComparison.InvariantCultureIgnoreCase))
            {
                entityValueFound = true;
            }
        }

        if (!entityValueFound && expectedCount > 0)
        {
            Assert.Fail($"Input \"{input}\" returned no entity with claim value \"{expectedClaimValue}\".");
        }

        if (expectedCount == Int32.MaxValue)
            expectedCount = entities.Count;

        Assert.AreEqual(expectedCount, entities.Count, $"Input \"{input}\" should have returned {expectedCount} entities, but it returned {entities.Count} instead.");
    }

    public static void TestValidationOperation(SPClaim inputClaim, bool shouldValidate, string expectedClaimValue)
    {
        string[] entityTypes = new string[] { "User" };

        PickerEntity[] entities = ClaimsProvider.Resolve(Context, entityTypes, inputClaim);

        int expectedCount = shouldValidate ? 1 : 0;
        Assert.AreEqual(expectedCount, entities.Length, $"Validation of entity \"{inputClaim.Value}\" should have returned {expectedCount} entity, but it returned {entities.Length} instead.");
        if (shouldValidate)
        {
            StringAssert.AreEqualIgnoringCase(expectedClaimValue, entities[0].Claim.Value, $"Validation of entity \"{inputClaim.Value}\" should have returned value \"{expectedClaimValue}\", but it returned \"{entities[0].Claim.Value}\" instead.");
        }
    }

    public static void TestAugmentationOperation(string claimType, string claimValue, bool isMemberOfTrustedGroup)
    {
        SPClaim inputClaim = new SPClaim(claimType, claimValue, ClaimValueTypes.String, SPOriginalIssuers.Format(SPOriginalIssuerType.TrustedProvider, UnitTestsHelper.SPTrust.Name));
        Uri context = new Uri(UnitTestsHelper.Context.AbsoluteUri);

        SPClaim[] groups = ClaimsProvider.GetClaimsForEntity(context, inputClaim);

        bool groupFound = false;
        if (groups != null && groups.Contains(TrustedGroup))
            groupFound = true;

        if (isMemberOfTrustedGroup)
            Assert.IsTrue(groupFound, $"Entity \"{claimValue}\" should be member of group \"{TrustedGroupToAdd_ClaimValue}\", but this group was not found in the claims returned by the claims provider.");
        else
            Assert.IsFalse(groupFound, $"Entity \"{claimValue}\" should NOT be member of group \"{TrustedGroupToAdd_ClaimValue}\", but this group was found in the claims returned by the claims provider.");
    }
}

public class SearchEntityDataSourceCollection : IEnumerable
{
    public IEnumerator GetEnumerator()
    {
        yield return new[] { "yvand", "2", "yvand@contoso.local" };
        yield return new[] { "IDoNotExist", "0", "" };
        yield return new[] { "group1", "1", @"contoso.local\group1" };
    }
}

public class SearchEntityDataSource
{
    public static IEnumerable<TestCaseData> GetTestData()
    {
        DataTable dt = DataTable.New.ReadCsv(UnitTestsHelper.DataFile_AllAccounts_Search);
        foreach (Row row in dt.Rows)
        {
            var registrationData = new SearchEntityData();
            registrationData.Input = row["Input"];
            registrationData.ExpectedResultCount = Convert.ToInt32(row["ExpectedResultCount"]);
            registrationData.ExpectedEntityClaimValue = row["ExpectedEntityClaimValue"];
            yield return new TestCaseData(new object[] { registrationData });
        }
    }

    //public class ReadCSV
    //{
    //    public void GetValue()
    //    {
    //        TextReader tr1 = new StreamReader(@"c:\pathtofile\filename", true);

    //        var Data = tr1.ReadToEnd().Split('\n')
    //        .Where(l => l.Length > 0)  //nonempty strings
    //        .Skip(1)               // skip header 
    //        .Select(s => s.Trim())   // delete whitespace
    //        .Select(l => l.Split(',')) // get arrays of values
    //        .Select(l => new { Field1 = l[0], Field2 = l[1], Field3 = l[2] });
    //    }
    //}
}

public class SearchEntityData
{
    public string Input;
    public int ExpectedResultCount;
    public string ExpectedEntityClaimValue;
}

public class ValidateEntityDataSource
{
    public static IEnumerable<TestCaseData> GetTestData()
    {
        DataTable dt = DataTable.New.ReadCsv(UnitTestsHelper.DataFile_AllAccounts_Validate);
        foreach (Row row in dt.Rows)
        {
            var registrationData = new ValidateEntityData();
            registrationData.ClaimValue = row["ClaimValue"];
            registrationData.ShouldValidate = Convert.ToBoolean(row["ShouldValidate"]);
            registrationData.IsMemberOfTrustedGroup = Convert.ToBoolean(row["IsMemberOfTrustedGroup"]);
            yield return new TestCaseData(new object[] { registrationData });
        }
    }
}

public class ValidateEntityData
{
    public string ClaimValue;
    public bool ShouldValidate;
    public bool IsMemberOfTrustedGroup;
}
