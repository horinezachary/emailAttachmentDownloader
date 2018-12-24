package com.horine.emailAttachmentDownloader;

import com.sun.mail.util.BASE64DecoderStream;
import com.sun.mail.util.BASE64EncoderStream;

import java.io.*;
import java.nio.charset.StandardCharsets;

public class GlobalSettings {
    private String cfgFilepath;
    private String fileName;
    private String saveFolder;
    private String popHost;
    private String storeType;
    private String account;
    private String password;
    private String[] keywords;
    private boolean onStartup;
    private int schedule;
    private long lastRuntime;

    /*
    @param host "pop.gmail.com";
    @param mailStoreType = "pop3";
    @param username = "abc@gmail.com";
    @param password = "*****";
     */

    GlobalSettings(String cfgFilepath){
        this.cfgFilepath = cfgFilepath;
        setFileName(cfgFilepath);
        saveFolder = null;
        popHost = "pop.gmail.com";
        storeType = "pop3";
        account = "";
        password = "";
        setKeywords("");
        this.onStartup = false;
        this.schedule = -1;
        this.lastRuntime = 0;
    }

    void getData() {
        String datain = "";
        File infile = new File(cfgFilepath);
        try {
            FileInputStream fis = new FileInputStream(infile);
            BASE64DecoderStream decodeStream = new BASE64DecoderStream(fis);
            InputStreamReader isr = new InputStreamReader(decodeStream, StandardCharsets.UTF_8);
            BufferedReader in = new BufferedReader(isr);
            String line;
            while ((line = in.readLine()) != null){
                datain += line;
            }
            in.close();

        } catch (IOException e) {
            e.printStackTrace();
        }

        System.out.println(datain);
        String[] splitvals = datain.split(";");
        saveFolder = splitvals[0];
        account = splitvals[1];
        password = splitvals[2];
        popHost = splitvals[3];
        storeType = splitvals[4];
        if (splitvals.length < 6){
            setKeywords("");
        }
        else {
            setKeywords(splitvals[5]);
        }
        setOnStartup(splitvals[6]);
        setFileName(cfgFilepath);
    }

    private void setFileName(String cfgFilepath) {
        this.fileName = cfgFilepath.substring(cfgFilepath.lastIndexOf("/")+1);
    }
    String getFileName(){
        return this.fileName;
    }

    private void setOnStartup(String str) {
        if (str.equals("true")){this.onStartup = true;}
        else if (str.equals("false")){this.onStartup = false;}
    }
    public void setOnStartup(boolean onStartup){
        this.onStartup = onStartup;
    }
    boolean getOnstartup(){
        return this.onStartup;
    }

    void saveData(){
        String dataout = "";
        dataout += saveFolder + ";"
                + account + ";"
                + password + ";"
                + popHost + ";"
                + storeType + ";"
                + getKeywordString() + ";"
                + onStartup + ";";
        File outFile = new File(cfgFilepath);
        try {
            FileOutputStream fos = new FileOutputStream(outFile);
            BASE64EncoderStream encodeStream = new BASE64EncoderStream(fos);
            OutputStreamWriter osw = new OutputStreamWriter(encodeStream, StandardCharsets.UTF_8);
            BufferedWriter out = new BufferedWriter(osw);
            out.write(dataout);
            out.close();
        } catch (IOException e) {
            e.printStackTrace();
        }

    }

    String getCfgFilepath() {
        return cfgFilepath;
    }

    void setCfgFilepath(String cfgFilepath) {
        this.cfgFilepath = cfgFilepath;
        setFileName(cfgFilepath);
    }

    String getSaveFolder() {
        return saveFolder;
    }

    void setSaveFolder(String saveFolder) {
        this.saveFolder = saveFolder;
    }

    String getPopHost() {
        return popHost;
    }

    void setPopHost(String popHost) {
        this.popHost = popHost;
    }

    String getStoreType() {
        return storeType;
    }

    void setStoreType(String storeType) {
        this.storeType = storeType;
    }

    String getAccount() {
        return account;
    }

    void setAccount(String account) {
        this.account = account;
    }

    String getPassword() {
        return password;
    }

    void setPassword(String password) {
        this.password = password;
    }

    String getKeywordString() {
        String keywordString = "";
        keywordString += keywords[0];
        for (int i = 1; i < keywords.length; i++){
            keywordString += "," + keywords[i];
        }
        return keywordString;
    }
    String[] getKeywords() {
        return keywords;
    }

    void setKeywords(String keywords) {
        String[] keywordArray = keywords.split(",");
        this.keywords = keywordArray;
    }
    public void setKeywords(String[] keywords) {
        this.keywords = keywords;
    }

    void splitKeywordString(String keywords){
        String splitString = "";

        String[] keywordArray = keywords.split(",");
        for (int i = 0; i < keywordArray.length; i++){
            if (keywordArray[i].length() == 0 || keywordArray[i].length() == 1 && keywordArray[i] == " "){
                continue;
            }
            else{
                String[] keywordspaceArray = keywordArray[i].split(" ");
                for (String s : keywordspaceArray) {
                    if (s.length() == 0 || s.length() == 1 && s == " ") {
                        continue;
                    } else if (!splitString.equals("")) {
                        splitString += "," + s;
                    } else {
                        splitString += s;
                    }
                }
            }
        }
        this.setKeywords(splitString);
    }

    public int getSchedule() {
        return schedule;
    }

    public void setSchedule(int schedule) {
        this.schedule = schedule;
    }

    public long getLastRuntime() {
        return lastRuntime;
    }

    public void setLastRuntime(long lastRuntime) {
        this.lastRuntime = lastRuntime;
    }
}
