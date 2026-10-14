using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Qizhu.Api.Modules.Identity;
using Qizhu.Api.Modules.Projects;
using Qizhu.Api.Modules.System;

namespace Qizhu.Api.Modules;

/// <summary>从路由发现阶段移除演示接口，防止生产环境误开放匿名业务接口。</summary>
public sealed class DevelopmentControllerFeatureProvider(bool enabled, bool sqlServerEnabled = false) : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        if (!enabled)
        {
            feature.Controllers.Remove(typeof(ProjectsController).GetTypeInfo());
        }
        if (!sqlServerEnabled)
        {
            feature.Controllers.Remove(typeof(WorkspaceProjectsController).GetTypeInfo());
            feature.Controllers.Remove(typeof(AuthController).GetTypeInfo());
            feature.Controllers.Remove(typeof(WorkspaceSystemController).GetTypeInfo());
        }
    }
}
