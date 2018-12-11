package com.horine.emailAttachmentDownloader;
public class GlobalSettings {
    private String cfgFilepath;

    private String saveFolder;
    private String popHost;
    private String storeType;
    private String account;
    private String password;
    private String[] keywords;



    public GlobalSettings(String cfgFilepath){
        this.cfgFilepath = cfgFilepath;
    }


    public String getCfgFilepath() {
        return cfgFilepath;
    }

    public void setCfgFilepath(String cfgFilepath) {
        this.cfgFilepath = cfgFilepath;
    }

    public String getSaveFolder() {
        return saveFolder;
    }

    public void setSaveFolder(String saveFolder) {
        this.saveFolder = saveFolder;
    }

    public String getPopHost() {
        return popHost;
    }

    public void setPopHost(String popHost) {
        this.popHost = popHost;
    }

    public String getStoreType() {
        return storeType;
    }

    public void setStoreType(String storeType) {
        this.storeType = storeType;
    }

    public String getAccount() {
        return account;
    }

    public void setAccount(String account) {
        this.account = account;
    }

    public String getPassword() {
        return password;
    }

    public void setPassword(String password) {
        this.password = password;
    }

    public String[] getKeywords() {
        return keywords;
    }
    public void setKeywords(String[] keywords) {
        this.keywords = keywords;
    }
}
