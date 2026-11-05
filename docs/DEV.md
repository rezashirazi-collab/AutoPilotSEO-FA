# Local development

- Run: `dotnet run --project src/AutopilotSeo.Web --launch-profile http` (http://localhost:5233)
- DB: `AutopilotSeo` on the dev SQL Server; connection string lives in user-secrets (`ConnectionStrings:Default`) of AutopilotSeo.Web.
- Migrations: `dotnet ef migrations add <Name> -p src/AutopilotSeo.Core -s src/AutopilotSeo.Web -o Migrations` then `dotnet ef database update -p src/AutopilotSeo.Core -s src/AutopilotSeo.Web`.

## Test account (dev DB only)

- Email: dev-test@autopilot.test
- Password: kept locally by the developer (not in the repo)
