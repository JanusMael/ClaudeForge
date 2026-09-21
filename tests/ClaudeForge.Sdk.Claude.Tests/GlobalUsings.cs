global using Microsoft.VisualStudio.TestTools.UnitTesting;
// ConfigScope lives on Core and is referenced by nearly every test here; the
// global using keeps it off each file's using block. Mirrors AgentForge.Sdk.Tests.
global using Bennewitz.Ninja.AgentForge.Core.Settings;
// PermissionOutcome is neutral vocabulary on Abstractions; the rest of the permission
// model stays Claude-side. Global so a test asserting an outcome reads the same as before.
global using Bennewitz.Ninja.AgentForge.Abstractions.Permissions;
