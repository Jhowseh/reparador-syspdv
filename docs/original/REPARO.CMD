cls
@tskill fbserver.exe
cls
c:
cls
CD\SYSPDV
del srv*
cls
del *log
cls
del *err
cls
del banc*
cls
del *txt
cls
@net stop firebirdserverdefaultinstance
@net start firebirdserverdefaultinstance
cls
@echo===============================================================================
@echo		Verificando integridade do sistema
@echo===============================================================================
@echo Aguarde... I / III
@gfix -v -f -user sysdba -password masterkey syspdv_srv.fdb
@net stop firebirdserverdefaultinstance
@net start firebirdserverdefaultinstance
cls
@echo===============================================================================
@echo		Verificando integridade do sistema
@echo===============================================================================
@echo Concluido... I / III
@echo Aguarde... II / III
@gfix -m -f -i -user sysdba -password masterkey syspdv_srv.fdb
@net stop firebirdserverdefaultinstance
@net start firebirdserverdefaultinstance
cls
@echo===============================================================================
@echo		Verificando integridade do sistema
@echo===============================================================================
@echo Concluido... I / III
@echo Concluido... II / III
@echo Aguarde... III / III
@gfix -v -f -user sysdba -password masterkey syspdv_srv.fdb
@pause
@echo===============================================================================
@echo    	Iniciando backup...
@echo===============================================================================
@net stop firebirdserverdefaultinstance
@net start firebirdserverdefaultinstance
@gbak -b -v -i -g -l -user sysdba -password masterkey syspdv_srv.fdb syspdv_srv.fbk
pause
@net stop firebirdserverdefaultinstance
cls
@MOVE SYSPDV_SRV.FDB bkpBd
@echo===============================================================================
@echo		Iniciando restore...
@echo===============================================================================
@net start firebirdserverdefaultinstance
@GBAK -c -v -i -user sysdba -password masterkey syspdv_srv.fbk syspdv_srv.fdb
@echo===============================================================================
@echo    	Ativando indices...
@echo===============================================================================
@VERIFICAINDICE.EXE
cls
@echo===============================================================================
@echo    	Atualizando banco de dados...
@echo===============================================================================
@start dbMonitor.exe
@atualizasyspdv_server.exe
cls
@echo================================================================================
@echo            Abrindo o sistema...
@echo================================================================================
@start syspdv_server
cls
exit