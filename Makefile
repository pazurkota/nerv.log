SHELL := /bin/bash

COVERAGE_MIN := 80
COVERAGE_EXCLUDE := %2a%2a/obj/%2a%2a/%2a.cs%2c%2a%2a/Migrations/%2a%2a/%2a.cs

.PHONY: deploy shell test coverage coverage-min

deploy:
	dotnet run --project nerv.log.Aspire/nerv.log.Aspire.csproj

shell:
	@echo "Aspire CLI shell — type commands without the leading 'aspire' (e.g. 'ps', 'logs'). Type 'exit' to quit."; \
	while read -e -p "aspire> " cmd; do \
		[ -z "$$cmd" ] && continue; \
		[ "$$cmd" = "exit" ] && break; \
		aspire $$cmd; \
	done

test:
	dotnet test nerv.log.Tests/nerv.log.Tests.csproj

coverage:
	dotnet test nerv.log.Tests/nerv.log.Tests.csproj \
		/p:CollectCoverage=true \
		/p:CoverletOutputFormat=cobertura \
		/p:ExcludeByFile=$(COVERAGE_EXCLUDE)

coverage-min:
	dotnet test nerv.log.Tests/nerv.log.Tests.csproj \
		/p:CollectCoverage=true \
		/p:CoverletOutputFormat=cobertura \
		/p:ExcludeByFile=$(COVERAGE_EXCLUDE) \
		/p:Threshold=$(COVERAGE_MIN) \
		/p:ThresholdType=line \
		/p:ThresholdStat=total
