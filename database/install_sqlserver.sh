#!/usr/bin/env bash
# install_sqlserver.sh
# Installs Microsoft SQL Server 2022 Developer Edition + sqlcmd/tools on
# Zorin OS 18.1 / Ubuntu 24.04 (x86_64). Run with sudo:
#   sudo bash database/install_sqlserver.sh
#
# After install, the script creates a database user 'csadmin' / password
# 'CsAdmin!2024' (CHANGE THIS for any non-demo use) and enables TCP 1433.
set -euo pipefail

export DEBIAN_FRONTEND=noninteractive

echo "==> Importing Microsoft GPG key + repo ..."
curl -fsSL https://packages.microsoft.com/keys/microsoft.asc \
  | gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg
# SQL Server 2022 engine lives in the dedicated mssql-server-2022 repo
# (jammy / Ubuntu 22.04). It installs cleanly on 24.04 (noble) with the
# right compat libs. The prod repo only carries mssql-tools.
echo "deb [arch=amd64,arm64 signed-by=/usr/share/keyrings/microsoft-prod.gpg] https://packages.microsoft.com/ubuntu/22.04/mssql-server-2022 jammy main" \
  > /etc/apt/sources.list.d/mssql-release.list

echo "==> Updating apt ..."
apt-get update

echo "==> Installing mssql-server dependencies (compat libs for noble) ..."
# noble ships libldap2 (2.6); SQL Server 2022 requires libldap-2.5-0, so we
# fetch the jammy .deb directly and install it with dpkg.
LDAP_DEB="libldap-2.5-0_2.5.16+dfsg-0ubuntu0.22.04.2_amd64.deb"
LDAP_URL="http://archive.ubuntu.com/ubuntu/pool/main/o/openldap/${LDAP_DEB}"
if ! dpkg -s libldap-2.5-0 >/dev/null 2>&1; then
  echo "Downloading ${LDAP_DEB} from jammy archive ..."
  curl -fsSL -o "/tmp/${LDAP_DEB}" "${LDAP_URL}"
  dpkg -i "/tmp/${LDAP_DEB}"
fi

echo "==> Installing mssql-server ..."
ACCEPT_EULA=Y apt-get install -y mssql-server

echo "==> Running mssql-conf setup (Developer edition) ..."
# Non-interactive setup: edition = Developer (2), accept EULA = Y
MSSQL_SA_PASSWORD='SqlServer!2024Dev' \
MSSQL_PID='Developer' \
/opt/mssql/bin/mssql-conf -n setup accept-eula >/dev/null

echo "==> Redirecting database data/log files onto the external drive ..."
# The engine lives on the system drive, but user database files (.mdf/.ldf)
# go onto the external drive. NOTE: mssql-conf rejects paths containing
# non-ASCII chars (the project folder has an emoji), so we use the drive
# root, which is ASCII-only.
DATA_DIR="/media/ebnzr/SSDrive_500GB/sqlserver-data"
mkdir -p "$DATA_DIR"
chown -R mssql:mssql "$DATA_DIR" 2>/dev/null || true
/opt/mssql/bin/mssql-conf set filelocation.defaultdatadir "$DATA_DIR"
/opt/mssql/bin/mssql-conf set filelocation.defaultlogdir "$DATA_DIR"

echo "==> Enabling + starting service ..."
systemctl enable mssql-server
systemctl restart mssql-server

echo "==> Installing sqlcmd + unixODBC dev tools ..."
ACCEPT_EULA=Y apt-get install -y mssql-tools18 unixodbc-dev
echo 'export PATH="$PATH:/opt/mssql-tools18/bin"' >> ~/.bashrc

echo "==> Waiting for SQL Server to accept connections ..."
for i in $(seq 1 30); do
  if /opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P 'SqlServer!2024Dev' -C -Q "SELECT 1" >/dev/null 2>&1; then
    echo "SQL Server is up."
    break
  fi
  sleep 2
done

SQLCMD="/opt/mssql-tools18/bin/sqlcmd -S localhost -U SA -P 'SqlServer!2024Dev' -C"

echo "==> Creating app login + database 'CustomerServiceDb' ..."
# NOTE: SQL Server password policy rejects passwords containing the login
# name, so we use a login-name-free complex password.
eval "$SQLCMD -Q \"
IF DB_ID('CustomerServiceDb') IS NULL CREATE DATABASE CustomerServiceDb;
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name='csadmin')
  CREATE LOGIN csadmin WITH PASSWORD = 'P@ssw0rd_2024_Xq';
ALTER SERVER ROLE sysadmin ADD MEMBER csadmin;
\""
eval "$SQLCMD -d CustomerServiceDb -Q \"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name='csadmin')
  CREATE USER csadmin FOR LOGIN csadmin;
ALTER ROLE db_owner ADD MEMBER csadmin;
\""

echo "DONE. SQL Server 2022 listening on TCP 1433."
echo "  SA password : SqlServer!2024Dev   (change for production)"
echo "  App login   : csadmin / P@ssw0rd_2024_Xq"
echo "Connection string (appsettings.json):"
echo "  Server=localhost,1433;Database=CustomerServiceDb;User Id=csadmin;Password=P@ssw0rd_2024_Xq;TrustServerCertificate=True;"
