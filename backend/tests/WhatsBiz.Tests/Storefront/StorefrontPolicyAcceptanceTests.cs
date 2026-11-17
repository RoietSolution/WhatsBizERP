using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontPolicyAcceptanceTests
{
    [Fact]
    public void TermsMigrationSeedsOnlyAnUnpublishedDraftAndKeepsAcceptanceHistoryEmpty()
    {
        var migration=Read("database/migrations/V84-StorefrontCustomerTermsAcceptance.sql");
        migration.Should().Contain("N'CUSTOMER_TERMS'").And.Contain("0,0,1,N'V84_DRAFT_SEED'")
            .And.Contain("N'PRIVACY_NOTICE'").And.Contain("0,0,1,N'V84_DRAFT_SEED'")
            .And.Contain("[RETAILER LEGAL NAME").And.Contain("[LEGAL REVIEW REQUIRED")
            .And.Contain("TR_StorefrontPolicies_ImmutablePublished")
            .And.Contain("TR_StorefrontPolicyAcceptances_Immutable")
            .And.Contain("UQ_StorefrontPolicyAcceptances_CustomerPolicy");
        migration.Should().NotContain("INSERT commerce.StorefrontPolicyAcceptances");
    }

    [Fact]
    public void AccountCreationChecksTheActiveTenantScopedPolicyInsideTheSignupTransaction()
    {
        var source=Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCustomerAuthenticationService.cs");
        source.Should().Contain("IsolationLevel.Serializable").And.Contain("WITH(UPDLOCK,HOLDLOCK)")
            .And.Contain("if (!input.IsRegistration) return null;")
            .And.Contain("ReadRequiredPolicy(connection, transaction, tenantId, \"CUSTOMER_TERMS\"")
            .And.Contain("ReadRequiredPolicy(connection, transaction, tenantId, \"PRIVACY_NOTICE\"")
            .And.Contain("RecordPolicyAcceptance(connection, transaction, tenantId, id, terms, storeKey")
            .And.Contain("RecordPolicyAcceptance(connection, transaction, tenantId, id, privacy, storeKey");
        source.IndexOf("if(await find.ExecuteScalarAsync(token) is Guid existing)",StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("ReadRequiredPolicy(connection, transaction, tenantId, \"CUSTOMER_TERMS\"",StringComparison.Ordinal));
    }

    [Fact]
    public void CustomerSignupInputCarriesSeparateTermsAndPrivacyAcknowledgements()
    {
        typeof(StorefrontOtpVerifyInput).GetProperties().Select(x=>x.Name).Should().Contain([
            "AcceptTerms","TermsPolicyId","TermsContentHash","AcknowledgePrivacy","PrivacyPolicyId","PrivacyContentHash"]);
    }

    [Fact]
    public void SignupUsesOneUncheckedCombinedConsentWithCurrentTenantPolicyLinks()
    {
        var ui=Read("frontend/KhataDhari.Customer/src/app/pages/customer-auth.page.html");
        ui.Split("type=\"checkbox\"",StringSplitOptions.None).Length.Should().Be(2);
        ui.Should().Contain("[checked]=\"termsAccepted\"")
            .And.Contain("I agree to the <a")
            .And.Contain("Terms &amp; Conditions</a> and acknowledge the <a")
            .And.Contain("Privacy Policy</a>.")
            .And.Contain("target=\"_blank\"")
            .And.Contain("[disabled]=\"policiesLoading||!policiesReady()||busy()\"");

        var signup=Read("frontend/KhataDhari.Customer/src/app/pages/customer-auth.page.ts");
        signup.Should().Contain("privacyAcknowledged:this.termsAccepted")
            .And.Contain("this.termsAccepted=false")
            .And.Contain("void this.loadPolicies(true)");

        var policyPage=Read("frontend/KhataDhari.Customer/src/app/pages/storefront-content.page.ts");
        policyPage.Should().Contain("Effective {{policyEffectiveDate}}")
            .And.Contain("this.auth.policies(this.storeKey)");
    }

    [Fact]
    public void PolicyPublicationIsOwnerOnlyAndRequiresApprovalAndPlaceholderResolution()
    {
        var publish=typeof(StorefrontPolicyController).GetMethod(nameof(StorefrontPolicyController.Publish))!;
        publish.GetCustomAttribute<PlatformAuthorizeAttribute>().Should().NotBeNull();
        var source=Read("backend/src/WhatsBiz.Api/Controllers/StorefrontPolicyController.cs");
        source.Should().Contain("ApprovalReference").And.Contain("Resolve all bracketed fields and placeholder markers")
            .And.Contain("HasUnresolvedPlaceholders").And.Contain("IsPublished=0 AND IsActive=0");
    }

    [Fact]
    public void StorefrontExposesPublicVersionedPolicyAndLegalRoutes()
    {
        var routes=Read("frontend/KhataDhari.Customer/src/app/app.routes.ts");
        routes.Should().Contain("terms-and-conditions").And.Contain("cancellation-return-refund").And.Contain("kind: 'privacy'");
        var controller=Read("backend/src/WhatsBiz.Api/Controllers/StorefrontPolicyController.cs");
        controller.Should().Contain("p.IsPublished=1 AND p.IsActive=1 AND p.AcceptanceRequired=1 AND p.EffectiveAtUtc<=SYSUTCDATETIME()");
        var contentPage=Read("frontend/KhataDhari.Customer/src/app/pages/storefront-content.page.ts");
        contentPage.Should().Contain("has not published its Terms").And.Contain("has not published a Privacy Notice")
            .And.Contain("x.isPublished&&x.acceptanceRequired").And.NotContain("DRAFT — NOT APPROVED FOR PUBLICATION");
        var ui=Read("frontend/KhataDhari.Customer/src/app/pages/customer-auth.page.html");
        ui.Should().Contain("[checked]=\"termsAccepted\"").And.Contain("target=\"_blank\"")
            .And.Contain("Terms &amp; Conditions").And.Contain("Privacy Policy");
    }

    [Fact]
    public void ApplicationOwnerHasAProtectedPolicyManagementScreen()
    {
        var routes=Read("frontend/WhatsBiz.Web/src/app/app.routes.ts");
        routes.Should().Contain("application-owner/customer-policies").And.Contain("StorefrontPolicyManagementComponent");
        var component=Read("frontend/WhatsBiz.Web/src/app/features/administration/storefront-policy-management.component.ts");
        component.Should().Contain("Approval reference").And.Contain("Effective date").And.Contain("Preview policy")
            .And.Contain("Unresolved placeholders found").And.Contain("Publish policy");
    }

    private static string Read(string relative)
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir is not null&&!File.Exists(Path.Combine(dir.FullName,"AGENTS.md")))dir=dir.Parent;
        return File.ReadAllText(Path.Combine(dir?.FullName??throw new InvalidOperationException(),relative.Replace('/',Path.DirectorySeparatorChar)));
    }
}
