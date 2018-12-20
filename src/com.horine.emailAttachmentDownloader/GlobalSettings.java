package com.horine.emailAttachmentDownloader;

import com.sun.mail.util.BASE64DecoderStream;
import com.sun.mail.util.BASE64EncoderStream;

import java.io.*;
import java.nio.charset.StandardCharsets;

public class GlobalSettings {
    private String cfgFilepath;

    private String saveFolder;
    private String popHost;
    private String storeType;
    private String account;
    private String password;
    private String[] keywords;



    GlobalSettings(String cfgFilepath){
        this.cfgFilepath = cfgFilepath;
        this.onStartup = false;
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
    }

    void saveData(){
        String dataout = "";
        dataout += saveFolder + ";"
                + account + ";"
                + password + ";"
                + popHost + ";"
                + storeType + ";"
                + getKeywordString() + ";";
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
}
