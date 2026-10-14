using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class SeedIdentityExampleTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    [Fact]
    public async Task Seed_HasStableUsers_Roles_Owners_AndGuidRelations()
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider
            .GetRequiredService<SecureLabDbContext>();
        var expected = new[]
        {
            (DbSeeder.AliceId, "alice", "Reporter"),
            (DbSeeder.BobId, "bob", "Reporter"),
            (DbSeeder.MorganId, "morgan", "Analyst"),
            (DbSeeder.AdminId, "admin", "Administrator")
        };
        foreach (var (id, name, role) in expected)
        {
            var user = await users.FindByIdAsync(id.ToString());
            Assert.NotNull(user);
            Assert.Equal(name, user.UserName);
            Assert.True(await users.IsInRoleAsync(user, role));
        }
        var alice = await db.Incidents.AsNoTracking()
            .SingleAsync(item => item.Id == DbSeeder.AliceIncidentId);
        var bob = await db.Incidents.AsNoTracking()
            .SingleAsync(item => item.Id == DbSeeder.BobIncidentId);
        Assert.Equal(DbSeeder.AliceId, alice.OwnerUserId);
        Assert.Equal(DbSeeder.BobId, bob.OwnerUserId);

        var relations = new[]
        {
            (typeof(Incident), "OwnerUserId"),
            (typeof(IncidentComment), "AuthorUserId"),
            (typeof(IncidentStatusHistory), "ChangedByUserId")
        };
        foreach (var (type, field) in relations)
        {
            var entity = db.Model.FindEntityType(type)!;
            var property = entity.FindProperty(field)!;
            Assert.Equal(typeof(Guid), property.ClrType);
            var fk = Assert.Single(entity.GetForeignKeys(),
                key => key.Properties.Contains(property));
            Assert.Equal(typeof(ApplicationUser), fk.PrincipalEntityType.ClrType);
        }
    }
}
