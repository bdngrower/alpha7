# Instalação em Linux (Oracle Linux / RHEL 9)

Recomendado: uma **VM pequena só para o conector** (1 vCPU, 1 GB RAM, 10 GB disco). Não instale no servidor do A7Pharma: é produção da Alpha7.

O pacote vem do artefato `A7FoodyConnector-linux-x64` do CI, ou de:

```bash
dotnet publish src/A7Foody.Service -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish-linux
```

Na VM, como root, instale o ICU (formatação pt-BR de valores), se ainda não houver:

```bash
dnf install -y libicu
```

Crie o usuário do serviço e as pastas:

```bash
useradd --system --no-create-home --shell /sbin/nologin a7foody
```

```bash
mkdir -p /opt/a7foody-connector /var/lib/a7foody-connector && chown a7foody:a7foody /var/lib/a7foody-connector && chmod 750 /var/lib/a7foody-connector
```

Copie o conteúdo de `publish-linux/` para `/opt/a7foody-connector/` e crie a configuração local, com tokens e conexão. Ela fica fora do pacote e não é sobrescrita em atualizações:

```bash
install -o a7foody -g a7foody -m 600 /dev/null /var/lib/a7foody-connector/appsettings.local.json
```

Exemplo do conteúdo (ver README principal):

```json
{
  "A7": { "ConnectionStrings": { "1": "Host=<ip-do-servidor-a7>;Port=5432;Database=<banco>;Username=<leitura>;Password=<senha>" } },
  "Stores": [ { "StoreId": "1", "Name": "Matriz", "FoodyToken": "<token>", "Enabled": true } ]
}
```

Teste antes de ligar o serviço. Os comandos abaixo não enviam pedidos:

```bash
sudo -u a7foody /opt/a7foody-connector/A7FoodyConnector testar-foody --loja 1
```

```bash
sudo -u a7foody /opt/a7foody-connector/A7FoodyConnector testar-a7 --loja 1
```

Ative o serviço:

```bash
cp deploy/linux/a7foody-connector.service /etc/systemd/system/ && systemctl daemon-reload && systemctl enable --now a7foody-connector
```

Acompanhe:

```bash
systemctl status a7foody-connector
```

```bash
journalctl -u a7foody-connector -f
```

Logs em arquivo: `/var/lib/a7foody-connector/logs/`. Banco local: `/var/lib/a7foody-connector/connector.db`.
