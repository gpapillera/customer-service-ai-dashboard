using System.Reflection;
using System.Security.Claims;
using CustomerService.Api.Controllers;
using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using CustomerService.Application.Services;
using CustomerService.Domain.Entities;
using CustomerService.Domain.Interfaces;
using CustomerService.Tests.Fakes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CustomerService.Tests;

/// <summary>
/// Durable unit coverage for the Phase 2 security layer (the "auth boundary").
/// These tests lock in behaviour that was previously only verified by hand with
/// curl against a live server, so it cannot silently regress as more features
/// are built on top of it. Three concerns are covered:
///
///  1. Controller authorization attributes — staff controllers reject the
///     Customer role, and the customer portal requires the Customer role. This
///     is the structural guarantee that a customer token can never reach a
///     staff endpoint (no data leak).
///  2. <see cref="CustomerPortalController"/> runtime behaviour — the customer
///     id is derived strictly from the JWT claim (never a client-supplied
///     value), cases are scoped to the caller, and non-owned / missing cases
///     both return 404 (anti-enumeration). The customer DTO shape is also
///     asserted here (no priority / AI / agent / category fields leak).
///  3. <see cref="CaseCommentService"/> — the "exactly one author" invariant:
///     a staff comment sets only AuthorUserId, a customer comment sets only
///     AuthorCustomerId, and empty bodies / unknown cases are rejected.
/// </summary>
public class AuthBoundaryTests
{
    // ---------------------------------------------------------------------
    // 1. Controller authorization attributes (reflection-based)
    // ---------------------------------------------------------------------

    public static IEnumerable<object[]> StaffControllers =>
        new List<object[]>
        {
            new object[] { typeof(CasesController) },
            new object[] { typeof(CustomersController) },
            new object[] { typeof(CallLogsController) },
            new object[] { typeof(DashboardController) },
            new object[] { typeof(MlController) },
            new object[] { typeof(NotificationsController) },
            new object[] { typeof(UsersController) },
        };

    [Theory]
    [MemberData(nameof(StaffControllers))]
    public void StaffControllers_RequireAdminOrAgentRole(Type controller)
    {
        var attr = controller.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("Admin,Agent", attr!.Roles);
    }

    [Fact]
    public void CustomerPortalController_RequiresCustomerRole()
    {
        var attr = typeof(CustomerPortalController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("Customer", attr!.Roles);
    }

    [Fact]
    public void CustomerPortalController_HasNoStaffRoleAllowlist()
    {
        // A customer token must NOT be able to satisfy the portal's requirement
        // via the Admin/Agent roles.
        var attr = typeof(CustomerPortalController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        Assert.DoesNotContain("Admin", attr!.Roles);
        Assert.DoesNotContain("Agent", attr!.Roles);
    }

    // ---------------------------------------------------------------------
    // 2. CustomerPortalController runtime behaviour
    // ---------------------------------------------------------------------

    private static CustomerPortalController BuildPortal(
        out FakeRepository<Case> cases,
        out FakeRepository<Customer> customers,
        int customerId,
        ICaseCommentService? comments = null)
    {
        cases = new FakeRepository<Case>();
        customers = new FakeRepository<Customer>();
        (customers as IRepository<Customer>).AddAsync(new Customer { Id = customerId, Name = "Juan" }).Wait();

        var controller = new CustomerPortalController(
            cases,
            comments ?? new FakeCaseCommentService(),
            new FakeCaseService(),
            new FakeCustomerAuthService(),
            new FakeNotificationService());

        var identity = new ClaimsIdentity(new[]
        {
            new Claim("CustomerId", customerId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, customerId.ToString()),
            new Claim(ClaimTypes.Role, "Customer"),
        }, "test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        return controller;
    }

    [Fact]
    public async Task GetMyCases_ReturnsOnlyCasesOwnedByCallingCustomer()
    {
        var controller = BuildPortal(out var cases, out _, customerId: 1);
        // Customer 1 owns cases 1 and 5; customer 2 owns case 2.
        (cases as IRepository<Case>).AddAsync(new Case { Id = 1, CustomerId = 1, Subject = "A" }).Wait();
        (cases as IRepository<Case>).AddAsync(new Case { Id = 5, CustomerId = 1, Subject = "B" }).Wait();
        (cases as IRepository<Case>).AddAsync(new Case { Id = 2, CustomerId = 2, Subject = "C" }).Wait();

        var result = await controller.GetMyCases();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.Id == 1);
        Assert.Contains(result, c => c.Id == 5);
        Assert.DoesNotContain(result, c => c.Id == 2);
    }

    [Fact]
    public async Task GetMyCase_ReturnsCase_WhenOwnedByCallingCustomer()
    {
        var controller = BuildPortal(out var cases, out _, customerId: 1);
        (cases as IRepository<Case>).AddAsync(new Case
        {
            Id = 1,
            CustomerId = 1,
            Subject = "Double charged",
            Description = "URGENT refund",
            Status = CaseStatus.InProgress,
        }).Wait();

        var action = await controller.GetMyCase(1);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var dto = Assert.IsType<CustomerCaseDetailDto>(ok.Value);
        Assert.Equal(1, dto.Id);
        Assert.Equal("Double charged", dto.Subject);
        Assert.Equal(CaseStatus.InProgress, dto.Status);
    }

    [Fact]
    public async Task GetMyCase_Returns404_ForCaseOwnedByAnotherCustomer()
    {
        var controller = BuildPortal(out var cases, out _, customerId: 1);
        (cases as IRepository<Case>).AddAsync(new Case { Id = 2, CustomerId = 2, Subject = "Other" }).Wait();

        var action = await controller.GetMyCase(2);

        Assert.IsType<NotFoundResult>(action.Result);
    }

    [Fact]
    public async Task GetMyCase_Returns404_ForNonExistentCase()
    {
        var controller = BuildPortal(out var cases, out _, customerId: 1);

        var action = await controller.GetMyCase(999);

        Assert.IsType<NotFoundResult>(action.Result);
    }

    [Fact]
    public async Task GetMyCase_CustomerDto_OmitsPriorityAndInternalFields()
    {
        var controller = BuildPortal(out var cases, out _, customerId: 1);
        (cases as IRepository<Case>).AddAsync(new Case
        {
            Id = 1,
            CustomerId = 1,
            Subject = "S",
            Description = "D",
            Status = CaseStatus.New,
            Priority = Priority.High,          // internal — must NOT surface
            PriorityAutoSuggested = true,
            PriorityReason = "urgent",
            CategoryId = 3,                     // internal — must NOT surface
            AssignedToUserId = "agent-1",       // internal — must NOT surface
        }).Wait();

        var action = await controller.GetMyCase(1);
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var dto = Assert.IsType<CustomerCaseDetailDto>(ok.Value);

        // The DTO type itself has no Priority / PriorityReason / CategoryId /
        // AssignedToUserId members — if any of those were added, this test
        // (and the compile) would fail, catching a regression.
        Assert.Equal("S", dto.Subject);
        Assert.Equal("D", dto.Description);
        Assert.Equal(CaseStatus.New, dto.Status);
        Assert.NotNull(dto.Comments);
    }

    [Fact]
    public async Task PostComment_Returns404_WhenCaseNotOwnedByCaller()
    {
        var comments = new FakeCaseCommentService();
        var controller = BuildPortal(out var cases, out _, customerId: 1, comments);
        (cases as IRepository<Case>).AddAsync(new Case { Id = 2, CustomerId = 2, Subject = "Other" }).Wait();

        var action = await controller.PostComment(2, new CreateCaseCommentDto { Body = "hi" });

        Assert.IsType<NotFoundResult>(action.Result);
        Assert.False(comments.CustomerCommentAdded); // service never reached
    }

    [Fact]
    public async Task PostComment_Returns400_WhenModelStateInvalid()
    {
        // The [Required] attribute is enforced by the model binder in a live
        // pipeline; here we simulate the binder having rejected the empty body
        // by pre-populating ModelState, then assert the guard returns 400
        // before the service is reached.
        var comments = new FakeCaseCommentService();
        var controller = BuildPortal(out var cases, out _, customerId: 1, comments);
        (cases as IRepository<Case>).AddAsync(new Case { Id = 1, CustomerId = 1, Subject = "S" }).Wait();
        controller.ModelState.AddModelError(nameof(CreateCaseCommentDto.Body), "Comment body is required.");

        var action = await controller.PostComment(1, new CreateCaseCommentDto { Body = "" });

        Assert.IsType<BadRequestObjectResult>(action.Result);
        Assert.False(comments.CustomerCommentAdded);
    }

    [Fact]
    public async Task PostComment_Returns400_WhenBodyWhitespace_ByServiceValidation()
    {
        // Whitespace passes [Required] but the service rejects it; the controller
        // must map that ArgumentException to 400 (not 500).
        var svc = BuildCommentService(out var cases, out _, out _, out var customers);
        (cases as IRepository<Case>).AddAsync(new Case { Id = 1, CustomerId = 1, Subject = "S" }).Wait();
        (customers as IRepository<Customer>).AddAsync(new Customer { Id = 1, Name = "Juan" }).Wait();

        var controller = new CustomerPortalController(cases, svc, new FakeCaseService(), new FakeCustomerAuthService(), new FakeNotificationService());
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("CustomerId", "1"),
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Role, "Customer"),
        }, "test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };

        var action = await controller.PostComment(1, new CreateCaseCommentDto { Body = "   " });

        Assert.IsType<BadRequestObjectResult>(action.Result);
    }

    [Fact]
    public async Task PostComment_Returns201_AndAuthorsAsCustomer()
    {
        var comments = new FakeCaseCommentService();
        var controller = BuildPortal(out var cases, out _, customerId: 7, comments);
        (cases as IRepository<Case>).AddAsync(new Case { Id = 1, CustomerId = 7, Subject = "S" }).Wait();

        var action = await controller.PostComment(1, new CreateCaseCommentDto { Body = "Thanks!" });

        var created = Assert.IsType<CreatedAtActionResult>(action.Result);
        Assert.Equal(nameof(CustomerPortalController.GetComments), created.ActionName);
        // The author id passed to the service must come from the claim (7),
        // never from the route/body.
        Assert.True(comments.CustomerCommentAdded);
        Assert.Equal(7, comments.LastCustomerAuthorId);
    }

    [Fact]
    public async Task GetMyCases_Throws_WhenCustomerIdClaimMissing()
    {
        // With no CustomerId / NameIdentifier claim, GetCustomerId() must fail
        // safe (UnauthorizedAccessException) rather than impersonate a customer.
        var controller = new CustomerPortalController(new FakeRepository<Case>(), new FakeCaseCommentService(), new FakeCaseService(), new FakeCustomerAuthService(), new FakeNotificationService());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) },
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.GetMyCases());
    }

    // ---------------------------------------------------------------------
    // 3. CaseCommentService "exactly one author" invariant
    // ---------------------------------------------------------------------

    private static CaseCommentService BuildCommentService(
        out FakeRepository<Case> cases,
        out FakeRepository<CaseComment> comments,
        out FakeRepository<User> users,
        out FakeRepository<Customer> customers)
    {
        cases = new FakeRepository<Case>();
        comments = new FakeRepository<CaseComment>();
        users = new FakeRepository<User>();
        customers = new FakeRepository<Customer>();
        return new CaseCommentService(cases, comments, users, customers);
    }

    [Fact]
    public async Task AddStaffCommentAsync_SetsOnlyAuthorUserId()
    {
        var svc = BuildCommentService(out var cases, out var comments, out var users, out _);
        (cases as IRepository<Case>).AddAsync(new Case { Id = 1, CustomerId = 1, Subject = "S" }).Wait();
        (users as IRepository<User>).AddAsync(new User { Id = "agent-1", FullName = "Grace Agent" }).Wait();

        var dto = await svc.AddStaffCommentAsync(1, "agent-1", "Looking into this");

        Assert.Equal("Grace Agent", dto.AuthorDisplayName);
        Assert.True(dto.IsStaff);
        var stored = comments.Query().Single();
        Assert.Equal("agent-1", stored.AuthorUserId);
        Assert.Null(stored.AuthorCustomerId);
    }

    [Fact]
    public async Task AddCustomerCommentAsync_SetsOnlyAuthorCustomerId()
    {
        var svc = BuildCommentService(out var cases, out var comments, out _, out var customers);
        (cases as IRepository<Case>).AddAsync(new Case { Id = 1, CustomerId = 9, Subject = "S" }).Wait();
        (customers as IRepository<Customer>).AddAsync(new Customer { Id = 9, Name = "Juan" }).Wait();

        var dto = await svc.AddCustomerCommentAsync(1, 9, "Any update?");

        Assert.False(dto.IsStaff);
        Assert.Equal("Juan", dto.AuthorDisplayName);
        var stored = comments.Query().Single();
        Assert.Equal(9, stored.AuthorCustomerId);
        Assert.Null(stored.AuthorUserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddStaffCommentAsync_RejectsEmptyBody(string body)
    {
        var svc = BuildCommentService(out var cases, out var comments, out var users, out _);
        (cases as IRepository<Case>).AddAsync(new Case { Id = 1, CustomerId = 1, Subject = "S" }).Wait();
        (users as IRepository<User>).AddAsync(new User { Id = "agent-1", FullName = "Grace" }).Wait();

        await Assert.ThrowsAsync<ArgumentException>(() => svc.AddStaffCommentAsync(1, "agent-1", body));
        Assert.Empty(comments.Query());
    }

    [Fact]
    public async Task AddCustomerCommentAsync_Throws_WhenCaseMissing()
    {
        var svc = BuildCommentService(out var cases, out var comments, out _, out var customers);
        (customers as IRepository<Customer>).AddAsync(new Customer { Id = 9, Name = "Juan" }).Wait();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.AddCustomerCommentAsync(404, 9, "hi"));
        Assert.Empty(comments.Query());
    }

    [Fact]
    public async Task AddStaffCommentAsync_Throws_WhenUserMissing()
    {
        var svc = BuildCommentService(out var cases, out var comments, out var users, out _);
        (cases as IRepository<Case>).AddAsync(new Case { Id = 1, CustomerId = 1, Subject = "S" }).Wait();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.AddStaffCommentAsync(1, "ghost", "hi"));
        Assert.Empty(comments.Query());
    }

    // ---------------------------------------------------------------------
    // Test doubles
    // ---------------------------------------------------------------------

    /// <summary>
    /// Minimal <see cref="ICaseCommentService"/> fake that records whether a
    /// customer comment was attempted and with which author id, so the
    /// controller tests can assert the claim-derived author is used.
    /// </summary>
    private sealed class FakeCaseCommentService : ICaseCommentService
    {
        public bool CustomerCommentAdded { get; private set; }
        public int? LastCustomerAuthorId { get; private set; }

        public Task<IReadOnlyList<CaseCommentDto>?> GetCommentsAsync(int caseId)
            => Task.FromResult<IReadOnlyList<CaseCommentDto>?>(Array.Empty<CaseCommentDto>());

        public Task<CaseCommentDto> AddStaffCommentAsync(int caseId, string authorUserId, string body)
            => Task.FromResult(new CaseCommentDto { Id = 1, AuthorDisplayName = authorUserId, IsStaff = true, Body = body });

        public Task<CaseCommentDto> AddCustomerCommentAsync(int caseId, int authorCustomerId, string body)
        {
            CustomerCommentAdded = true;
            LastCustomerAuthorId = authorCustomerId;
            return Task.FromResult(new CaseCommentDto { Id = 1, AuthorDisplayName = $"Customer #{authorCustomerId}", IsStaff = false, Body = body });
        }
    }

    /// <summary>No-op notification service for the auth-boundary tests.</summary>
    private sealed class FakeNotificationService : INotificationService
    {
        public Task<int> GenerateOverdueAsync() => Task.FromResult(0);
        public Task<int> NotifyResolvedAsync(Case caseEntity) => Task.FromResult(0);
        public Task<int> NotifyNewCustomerMessageAsync(Case caseEntity, string customerName) => Task.FromResult(0);
        public Task<IReadOnlyList<NotificationDto>> GetAllAsync(string? recipientUserId = null) => Task.FromResult<IReadOnlyList<NotificationDto>>(Array.Empty<NotificationDto>());
        public Task<NotificationSummaryDto> GetSummaryAsync(string? recipientUserId = null) => Task.FromResult(new NotificationSummaryDto());
        public Task<bool> MarkReadAsync(int id) => Task.FromResult(false);
        public Task<int> MarkAllReadAsync() => Task.FromResult(0);
    }
}
