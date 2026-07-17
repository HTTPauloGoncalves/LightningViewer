# Monitoramento de Raios MTG-LI (BlueOcean)

Este sistema é uma aplicação web completa para monitoramento em tempo quase-real de descargas atmosféricas (raios), consumindo dados da agência Europeia EUMETSAT (satélite MTG-LI). 

O sistema coleta dados brutos via API externa, filtra descargas ocorridas na América do Sul e em unidades tomadoras específicas, e fornece uma interface de mapa (web) visual e interativa para acompanhamento "Ao Vivo" e histórico das últimas 3 horas.

---

## 🛠 Arquitetura do Projeto

*   **Front-end**: HTML, CSS, JavaScript Vanilla e Leaflet (para o mapa interativo). Totalmente responsivo.
*   **Back-end**: ASP.NET Core 8 (MVC e Web API).
*   **Banco de Dados**: PostgreSQL (usando Entity Framework Core).
*   **Ingestor de Dados**: Um Worker em segundo plano (`BackgroundService`) roda nativamente junto com o servidor web, consultando a EUMETSAT a cada 5 minutos, baixando arquivos NetCDF-4 e processando em memória.

**Diferenciais**:
*   O back-end possui cache de memória (`IMemoryCache`) garantindo que as consultas geográficas não sobrecarreguem o banco de dados.
*   O banco se "limpa" automaticamente (dados com mais de 3 horas são apagados).
*   **Migrações Automáticas**: O banco é criado e configurado automaticamente quando a aplicação sobe (não é necessário rodar scripts SQL manualmente).

---

## 🚀 Como hospedar este projeto do zero

Este guia supõe que você possui um servidor (ex: VPS Linux Ubuntu, AWS EC2, DigitalOcean) ou vai rodar em um serviço de nuvem como Azure App Service.

### Passo 1: Pré-requisitos do Servidor

Para rodar este projeto, o seu servidor precisa ter instalados:
1.  **.NET 8 Runtime** (ou SDK se for compilar no próprio servidor).
2.  **PostgreSQL** (versão 12 ou superior).

Se estiver usando Linux (Ubuntu), você pode instalar o PostgreSQL com:
```bash
sudo apt update
sudo apt install postgresql postgresql-contrib
```

### Passo 2: Criando o Banco de Dados

A aplicação cria as tabelas automaticamente, mas ela precisa de um banco vazio para se conectar.
Acesse o PostgreSQL e crie um banco de dados:

```sql
-- Acesse o PostgreSQL
sudo -u postgres psql

-- Rode os seguintes comandos:
CREATE DATABASE db_raios;
CREATE USER admin_raios WITH ENCRYPTED PASSWORD 'suasenhaforte123';
GRANT ALL PRIVILEGES ON DATABASE db_raios TO admin_raios;
-- (Saia do psql digitando \q)
```

### Passo 3: Configurando a Aplicação

O projeto lê as configurações de conexão através de um arquivo chamado `appsettings.json` ou de **Variáveis de Ambiente** (Recomendado para produção).

**Opção A: Variáveis de Ambiente (Recomendado)**
No seu servidor Linux, antes de rodar a aplicação, defina a variável:
```bash
export ConnectionStrings__DefaultConnection="Host=localhost;Database=db_raios;Username=admin_raios;Password=suasenhaforte123"
```

**Opção B: Alterando o `appsettings.json`**
Abra o arquivo `appsettings.json` na raiz do projeto compilado e altere a chave `"DefaultConnection"`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=db_raios;Username=admin_raios;Password=suasenhaforte123"
  },
  "EumetSat": {
    "RetentionHours": 3
  }
}
```

### Passo 4: Publicando (Compilando) o Projeto

Na máquina onde está o código fonte (seu PC), você deve gerar a versão de produção. Abra o terminal na pasta do projeto (`LightningViewer.Web`) e rode:

```bash
dotnet publish -c Release -o ./publish
```

Isso criará uma pasta chamada `publish`. Pegue **todos** os arquivos dentro desta pasta e envie para o seu servidor via FTP/SFTP (ex: coloque-os na pasta `/var/www/raios`).

### Passo 5: Rodando a Aplicação no Servidor (Produção)

No seu servidor, navegue até a pasta onde você enviou os arquivos e execute a aplicação:

```bash
cd /var/www/raios
dotnet LightningViewer.Web.dll
```

> **MÁGICA DO BACK-END:** 
> Assim que você rodar esse comando pela primeira vez, o código C# vai se conectar no PostgreSQL, criar todas as tabelas (Flashes, ProcessedFiles) e já vai dar um "Seed" (inserir as unidades pré-cadastradas no banco). Imediatamente, o Ingestor vai iniciar um **Backfill**, baixando os raios das últimas 3 horas e populando o mapa. O site já estará funcionando!

### Passo 6: Mantendo o site no ar (Serviço do Windows ou Systemd no Linux)

Se você fechar o terminal, o site cai. Para manter ele sempre online e ligar sozinho se o servidor reiniciar, crie um serviço. No Linux (Ubuntu):

1. Crie um arquivo de serviço: `sudo nano /etc/systemd/system/raios.service`
2. Cole o conteúdo:
```ini
[Unit]
Description=Monitoramento de Raios BlueOcean

[Service]
WorkingDirectory=/var/www/raios
ExecStart=/usr/bin/dotnet /var/www/raios/LightningViewer.Web.dll
Restart=always
# Reinicia se fechar por falha
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=monitor-raios
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ConnectionStrings__DefaultConnection="Host=localhost;Database=db_raios;Username=admin_raios;Password=suasenhaforte123"

[Install]
WantedBy=multi-user.target
```
3. Ative e inicie o serviço:
```bash
sudo systemctl enable raios.service
sudo systemctl start raios.service
```

### Passo 7: Proxy Reverso e SSL (Opcional, mas recomendado)

O Kestrel (servidor web interno do .NET) estará rodando internamente na porta `5000` ou similar. É recomendado usar o **Nginx** ou **Apache** para rotear a porta 80 (HTTP) e 443 (HTTPS) para o .NET e aplicar os certificados SSL do seu domínio (ex: raios.previsoesblueocean.com.br).

---

## 🛡️ Dica de Segurança e Escalabilidade
Atualmente, qualquer um que possua o link do front-end pode visualizar o painel. Se for uma ferramenta restrita ou vendida pela BlueOcean, você deve implementar autenticação no `LightningController.cs` usando Tokens JWT ou Cookie Authentication do ASP.NET.
