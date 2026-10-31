# Spec Delta

## Purpose

Публикация машиночитаемого описания HTTP API и интерактивной страницы над ним, чтобы смотреть и вызывать эндпойнты из
браузера в любой среде.

## ADDED Requirements

### Requirement: OpenAPI-документ

Система SHALL отдавать описание HTTP API в формате OpenAPI по адресу `/openapi/v1.json` во всех средах, включая
Production. Документ описывает все эндпойнты, которые обслуживает API.

#### Scenario: Документ в Production

- **WHEN** сервис запущен в среде Production и клиент запрашивает `GET /openapi/v1.json`
- **THEN** система отвечает `200` с OpenAPI-документом, в котором есть путь `/api/market-data/candles`

### Requirement: Swagger UI

Система SHALL отдавать по адресу `/swagger` интерактивную страницу Swagger UI, построенную по документу
`/openapi/v1.json`, во всех средах, включая Production. Страница доступна без авторизации.

#### Scenario: Страница в Production

- **WHEN** сервис запущен в среде Production и клиент запрашивает `GET /swagger/index.html`
- **THEN** система отвечает `200` с HTML-страницей Swagger UI

#### Scenario: Страница указывает на документ API

- **WHEN** клиент открывает Swagger UI
- **THEN** страница загружает описание API из `/openapi/v1.json`
