.PHONY: deploy test

deploy:
	dotnet run --project nerv.log.Aspire/nerv.log.Aspire.csproj

test:
	dotnet test nerv.log.Tests/nerv.log.Tests.csproj
