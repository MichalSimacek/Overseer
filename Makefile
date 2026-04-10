SHELL := /bin/bash

.PHONY: help backend-build frontend-install frontend-build up down smoke

help:
	@echo "make backend-build     # build ASP.NET API"
	@echo "make frontend-install  # install frontend dependencies"
	@echo "make frontend-build    # build React app"
	@echo "make up                # run full docker-compose stack"
	@echo "make down              # stop stack"
	@echo "make smoke             # basic API smoke checks"

backend-build:
	dotnet build backend/Overseer.Api/Overseer.Api.csproj

frontend-install:
	npm ci --prefix frontend

frontend-build: frontend-install
	npm run build --prefix frontend

up:
	docker compose up --build -d

down:
	docker compose down --remove-orphans

smoke:
	curl -fsS http://localhost:8080/health
	curl -fsS http://localhost:8080/api/public/pricing
