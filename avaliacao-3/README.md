# Avaliacao 3 - Replicacao de dados

## Etapa 5: quorum e confirmacao ao cliente

Aplicacao .NET 10 com quatro instancias do mesmo servidor gRPC e um cliente.
Um lider fixo recebe escritas; os seguidores buscam o log via `Fetch` e mantem
seus proprios arquivos JSON. O log continua separado dos dados comitados.

O lider exige **quorum fixo de tres nos (ele mesmo e dois seguidores)** antes
de comitar uma escrita e responder sucesso ao cliente. Atividade e confirmacao
de armazenamento sao acompanhadas separadamente. Nao ha ainda eleicao ou
truncamento de logs divergentes; nao troque a lideranca por configuracao.

### Executar

Requer SDK .NET 10. Na raiz do repositorio, compile antes de iniciar os processos:
Encerre os nos e o cliente da versao anterior antes de compilar; no Windows,
os executaveis abertos podem bloquear a substituicao das DLLs. Reinicie todos
com a mesma versao, sem misturar etapas.

```powershell
dotnet build avaliacao-3\Replication.slnx
```

Execute cada comando em um terminal separado, mantendo os quatro abertos.
Os caminhos `cluster` abaixo evitam reutilizar estados antigos sem log:

```powershell
dotnet run --no-build --project avaliacao-3\src\Replication.Node -- --node-id node-1 --role leader --port 7001 --data avaliacao-3\data\cluster\node-1\state.json
```

```powershell
dotnet run --no-build --project avaliacao-3\src\Replication.Node -- --node-id node-2 --role follower --leader http://127.0.0.1:7001 --port 7002 --data avaliacao-3\data\cluster\node-2\state.json
```

```powershell
dotnet run --no-build --project avaliacao-3\src\Replication.Node -- --node-id node-3 --role follower --leader http://127.0.0.1:7001 --port 7003 --data avaliacao-3\data\cluster\node-3\state.json
```

```powershell
dotnet run --no-build --project avaliacao-3\src\Replication.Node -- --node-id node-4 --role follower --leader http://127.0.0.1:7001 --port 7004 --data avaliacao-3\data\cluster\node-4\state.json
```

Em um quinto terminal:

```powershell
dotnet run --no-build --project avaliacao-3\src\Replication.Client -- --server http://127.0.0.1:7001
```

O endereco padrao do cliente e `http://127.0.0.1:7001`, e a porta padrao do
servidor e `7001`. Sem configuracao, o no continua sendo lider fixo `node-1`,
mas nao confirma escritas sozinho: sao necessarios dois seguidores ativos.
Cada seguidor exige `--node-id` exclusivo, `--role follower` e `--leader`.
Cada instancia deve usar porta e arquivo diferentes.
Os membros fixos sao `node-1,node-2,node-3,node-4`. Para usar outros nomes,
informe a mesma lista `--members id1,id2,id3,id4` em todos os processos.
Identidades desconhecidas e repeticoes da mesma identidade nao aumentam o quorum.
Esta etapa assume nos confiaveis: nao implementa autenticacao de identidades.
O servidor aceita apenas conexoes locais IPv4, via HTTP/2
sem TLS (h2c), para desenvolvimento. Nao exponha essa configuracao na Internet.
O terminal do servidor exibe logs; o menu agora fica somente no cliente:

- `1`: informar a chave e depois o valor; uma chave existente e atualizada.
- `2`: consultar uma chave.
- `3`: listar os dados comitados em ordem de chave.
- `4`: inspecionar log, commit e, no lider, quorum, nos ativos e indices
  confirmados por seguidor. O seguidor nao estima o quorum global.
- `0`: sair.

As chaves diferenciam maiusculas de minusculas e nao podem ser vazias ou conter
somente espacos. Os valores podem ser vazios. As entradas sao lidas como texto,
uma linha por campo.

O caminho informado ao servidor e relativo ao diretorio de execucao. Sem `--data`, o arquivo
fica em `data\state.json` dentro do diretorio do executavel. O caminho absoluto
e exibido na inicializacao. Use o mesmo caminho para recuperar os dados.

### Verificar a recuperacao apos reinicio

1. Inicie os quatro servidores e o cliente nos terminais separados.
2. No cliente, escolha `1`, informe `produto:1` e depois `Teclado`.
3. Escolha `2` e consulte `produto:1`; escolha `3` para listar.
4. Abra um cliente com `--server http://127.0.0.1:7002` e consulte/lista os dados.
   A convergencia e eventual, nao necessariamente imediata apos a escrita.
5. Escolha `4` em cada no para ver seu papel, log e commit.
6. Encerre o node-4 com `Ctrl+C`, faca novas escritas no lider e reinicie node-4
   com o mesmo comando. Ele recupera o JSON e busca apenas as entradas faltantes.
7. Encerre o lider: seguidores continuam lendo os dados ja comitados, mas nao
   elegem substituto. Reinicie o lider usando o MESMO JSON para retomar o Pull.
8. Escrita em um seguidor retorna `FailedPrecondition` e indica o endereco do
   lider. O cliente mostra o comando `--server`; nao ha redirecionamento automatico.

### Demonstrar perda e recuperacao de quorum

1. Com os quatro nos ligados, use `4` no cliente conectado ao lider para
   conferir `quorum disponivel`, e grave um dado com a opcao `1`.
2. Desligue node-4: com lider, node-2 e node-3, novas escritas ainda funcionam.
3. Desligue node-3 e aguarde mais de 600ms. Agora so ha dois nos ativos.
4. Confira `quorum indisponivel`. Uma escrita nova retorna `Unavailable`
   sem adicionar uma entrada ao log. Leituras comitadas continuam funcionando.
5. Reinicie node-3 com o mesmo JSON. Apos os Fetchs restabelecerem o quorum,
   escritas voltam a funcionar. Node-4 pode voltar depois e recuperar seu atraso.

O [QuorumCoordinator](src/Replication.Node/Replication/QuorumCoordinator.cs)
mantem o ultimo Fetch e o ultimo indice confirmado de cada membro na memoria
do lider. Mais de 600ms sem Fetch valido torna a replica inativa; um monitor
verifica isso a cada 100ms, e toda nova escrita tambem consulta o tempo atual.
Ao reiniciar o lider, as replicas precisam se apresentar novamente: atividade
antiga nao e recuperada do JSON.

Uma escrita aceita e primeiro persistida no log SEM commit. O indice de commit
so avanca quando dois seguidores distintos informam indices que incluem essa
entrada, alem da copia local do lider. Entregar uma resposta Fetch nao e prova
de armazenamento. O commit e sua aplicacao sao persistidos antes de liberar a
resposta de sucesso ao cliente. Escritas concorrentes aguardam seus proprios
indices sem bloquear a chegada de Fetchs.

Sem quorum inicial, a escrita e recusada sem alterar o log. Se o quorum se perder
durante a espera, a chamada falha; se os nos estiverem ativos mas atrasados,
o servidor espera no maximo cinco segundos e retorna `DeadlineExceeded`.
O deadline do cliente pode encerrar a chamada antes. Uma entrada ja persistida
permanece pendente e pode ser comitada quando as replicas voltarem. Falha ou
timeout nao significa cancelamento definitivo da operacao: nao ha rollback
nem repeticao automatica. Confira o log e a chave antes de repetir.
Falha de persistencia do commit impede sucesso e desabilita novas escritas
ate a correcao do armazenamento e reinicio do lider.

### Fluxo Pull

O [PullReplicationWorker](src/Replication.Node/Replication/PullReplicationWorker.cs)
usa `BackgroundService`, um canal gRPC reutilizado e `PeriodicTimer` de 100ms.
Cada Fetch tem deadline de 2s; indisponibilidade/timeout gera aviso e novas
tentativas. A recuperacao da conexao tambem e registrada, sem repetir o mesmo
aviso a cada tentativa. Leituras locais continuam disponiveis.

1. Seguidor informa identidade, ultimo indice e termo persistidos.
2. Lider valida a posicao e retorna ate 128 entradas e seu indice de commit.
3. Seguidor persiste o lote SEM aplicar os valores.
4. No proximo Fetch, confirma a posicao que ja esta em disco.
5. Aplica somente o prefixo confirmado nessa requisicao, limitado pelo commit
   retornado pelo lider. Entradas recem-recebidas aguardam o proximo Fetch.

O lider registra cada posicao confirmada no Fetch e calcula o terceiro maior
indice entre sua copia local e as replicas ativas; esse e o limite do commit.

O lider nao abre conexoes nem envia dados espontaneamente aos seguidores.
Nao ha dependencia de RabbitMQ, Docker, Redis ou Kubernetes.
O termo continua sendo `0` ate a etapa de eleicao. Os logs exibem papel e progresso;
Fetchs sem mudancas ficam no nivel Debug. Falhas permanentes de protocolo,
estado ou persistencia sao registradas e interrompem o host do seguidor.

### Contrato e tratamento de erros

O [storage.proto](src/Replication.Contracts/Protos/storage.proto) define os RPCs:

| RPC | Entrada | Resultado |
| --- | --- | --- |
| `Write` | Chave e valor | Indice do log e indices de commit/aplicacao, apos persistir e comitar |
| `Read` | Chave | Valor, ou status `NotFound` |
| `List` | Requisicao vazia | Entradas ordenadas por chave |
| `GetLog` | Requisicao vazia | Log, indices, papel e estado de quorum/progresso das replicas no lider |
| `ReplicationTransport.Fetch` | Identidade, ultimo indice e termo do seguidor | Lote do log e commit do lider |

O cliente le e lista somente o estado aplicado. `GetLog` e apenas para
inspecao: revela quais entradas estao no log e se ja foram comitadas.

O projeto `Replication.Contracts` gera as mensagens, a base do servidor e o
cliente gRPC a partir do contrato. O servidor usa ASP.NET Core gRPC; o cliente
usa `Grpc.Net.Client`. Nao ha REST ou acesso do cliente ao arquivo JSON.

Chaves invalidas sao rejeitadas tambem no servidor com `InvalidArgument`.
Valor vazio e permitido e nao se confunde com chave inexistente.
Falhas de persistencia retornam `Internal`, sem confirmar sucesso nem aplicar
entradas cujo commit falhou; os detalhes ficam nos logs do servidor.
Ausencia de quorum retorna `Unavailable`, com explicacao no cliente.

Cada chamada do cliente tem deadline de cinco segundos, configuravel:

```powershell
dotnet run --project avaliacao-3\src\Replication.Client -- --server http://127.0.0.1:7001 --timeout-seconds 10
```

O cliente informa chave inexistente, servidor indisponivel e timeout, mantendo
o menu disponivel. `Ctrl+C` cancela uma chamada em andamento. Nao ha repeticao
automatica: se a comunicacao falhar durante uma escrita, o servidor pode ja
ter persistido o dado. Consulte a chave antes de repetir.

### Formato persistido

```json
{
  "formatVersion": 2,
  "commitIndex": 1,
  "lastApplied": 1,
  "log": [
    {
      "index": 1,
      "term": 0,
      "key": "produto:1",
      "value": "Teclado"
    }
  ],
  "data": {
    "produto:1": "Teclado"
  }
}
```

`log` guarda as operacoes persistidas, inclusive as ainda nao comitadas.
`commitIndex` e o maior indice efetivado; `lastApplied` indica ate onde o estado
final foi atualizado. `data` contem somente os valores ja aplicados. Uma entrada
com indice maior que `commitIndex` nao aparece em `Read` nem em `List`.

O armazenamento fornece `AppendUncommitted`, que persiste a entrada sem alterar
`data`, e `CommitThrough`, que aplica em ordem as entradas ate o indice indicado.
A aplicacao de dados, `commitIndex` e `lastApplied` e salva em uma substituicao
atomica do JSON. Um erro ao persistir o commit nao torna a entrada visivel. A
operacao gRPC `Write` persiste a entrada e aguarda confirmacoes nos Fetchs.
O coordenador de quorum chama `CommitThrough` apos obter tres copias; a resposta
de sucesso so e liberada depois de persistir esse commit.

Entradas usam indice sequencial e termo; como eleicao ainda nao foi implementada,
as escritas atuais usam termo `0`. `GetLog` permite inspecionar o estado do log;
os testes cobrem entrada nao comitada persistida, invisivel apos reinicio, e sua
aplicacao depois de `CommitThrough`.

O formato anterior, que continha apenas `data`, e carregado preservando os
valores existentes. Como esse formato nao registrava historico, o log comeca
vazio apos a migracao, e novas operacoes recebem indices a partir de `1`.
Esses dados legados nao podem ser replicados apenas pelo log. Para o cluster,
use novos caminhos como os exemplos acima e reinsira os dados pelo cliente.
Nao apague seu arquivo antigo. O Fetch rejeita explicitamente estado comitado
que nao possa ser reproduzido pelo log. Nao troque o lider fixo nem reutilize
arquivos de clusters diferentes; divergencias de indice/termo sao rejeitadas
sem apagar logs, e a reconciliacao completa pertence a uma etapa posterior.

O armazenamento usa `System.Text.Json`, sem banco de dados ou dependencias
externas de persistencia. A logica fica em
[JsonKeyValueStore](src/Replication.Node/Storage/JsonKeyValueStore.cs),
separada do [servico gRPC](src/Replication.Node/Services/StorageService.cs) e do
[menu do cliente](src/Replication.Client/Program.cs).

Cada atualizacao grava uma nova versao completa em um arquivo `.tmp`, faz
flush para disco e substitui o JSON oficial no mesmo diretorio. A memoria so e
atualizada depois da substituicao. Isso evita sobrescrever parcialmente o
arquivo oficial; nao constitui garantia absoluta contra perda de energia ou
falhas de hardware. O custo de reescrever todos os dados e adequado a esta etapa.

Um arquivo `.lock`, mantido aberto com acesso exclusivo, impede duas instancias
de usarem simultaneamente o mesmo estado. Ele pode permanecer no disco apos a
saida: o bloqueio e liberado ao fechar o processo, nao ao apagar o arquivo.
Cada futuro no deve usar seu proprio caminho.

JSON invalido interrompe a inicializacao sem apagar os dados. Falhas de escrita
sao registradas e retornadas ao cliente sem confirmar sucesso. Um `.tmp` abandonado
nao e promovido automaticamente; a proxima escrita o substitui.
Os dados da pasta local `data` nao devem ser versionados.

### Compilar e testar

```powershell
dotnet build avaliacao-3\Replication.slnx
dotnet test avaliacao-3\tests\Replication.Tests
```

Para validar em Release, acrescente `--configuration Release` aos comandos.
Ao executar com `--no-build`, use a mesma configuracao usada na compilacao.

Os testes locais cobrem gravacao, recuperacao, migracao do formato anterior,
entrada nao comitada, commit e aplicacao, validacao, exclusividade, falha de
persistencia e concorrencia. Os testes gRPC iniciam um servidor real em porta
dinamica e verificam os RPCs, indices reportados, leitura de somente dados
comitados, visibilidade do log, reinicio, falhas e indisponibilidade.
Um transporte atrasado de teste verifica o deadline sem introduzir atrasos
artificiais no servidor de producao.
Os testes Pull iniciam quatro hosts com portas dinamicas e arquivos separados,
verificam convergencia, bloqueio de escrita no seguidor, recuperacao do seguidor,
retomada apos reinicio do lider, lotes, confirmacao de persistencia e isolamento
de entradas nao comitadas. Tambem validam lotes invalidos e logs divergentes.
Os testes de quorum verificam confirmacoes distintas, espera da resposta gRPC,
expiracao de heartbeat com relogio controlado, rejeicao sem adicionar ao log,
timeout, cancelamento e falha de persistencia. A integracao verifica perda e
recuperacao do quorum com quatro hosts gRPC e JSONs independentes.
O [NuGet.Config](NuGet.Config) limita a restauracao desta avaliacao ao nuget.org,
incluindo os pacotes de teste, sem alterar a configuracao das outras avaliacoes.
A regra existente em [.gitignore](../.gitignore) ignora `avaliacao-3/tests/**`;
os testes permanecem locais e essa escolha de versionamento foi preservada.

### Proxima etapa

Passo 6: substituir a lideranca fixa por eleicao, com termo persistido, voto
unico por termo e validacao de atualizacao do log do candidato.
Depois, reconciliar logs divergentes e completar os cenarios de recuperacao.
