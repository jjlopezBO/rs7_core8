begin 

 MATRICIAL_GENERACION96( trunc(sysdate)  );
 commit ; 
 MATRICIAL_GENERACION( trunc(sysdate)  );
 commit ;

 MATRICIAL_TRANSFERENCIAS96( trunc(sysdate) )  ;
 commit ;
  MATRICIAL_TRANSFERENCIAS(trunc(sysdate) )  ;
 commit ;

CARGADATOSBI( trunc(sysdate)  );
commit ; 

 delete from TDM_TD30_BULK ;
 commit ;
end;
/
exit