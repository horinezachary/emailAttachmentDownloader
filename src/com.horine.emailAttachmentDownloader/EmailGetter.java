package com.horine.emailAttachmentDownloader;
public class EmailGetter {

    static String host;
    static String storeType;
    static String user;
    static String password;
    static Properties properties;

    static ImageSaver imageSaver;

    /*
    @param host "pop.gmail.com";
    @param mailStoreType = "pop3";
    @param username = "abc@gmail.com";
    @param password = "*****";
     */

    public EmailGetter(String pop3Host, String storeType, String user, String password, ImageSaver imageSaver) {
    }
    public static void fetch(String[] keywords) {
    }
    /*
     * This method checks for content-type
     * based on which, it processes and
     * fetches the content of the message
     */
    public static void writePart(Part p) throws Exception {
    }
    public static void writeEnvelope(Message m) throws Exception {
    }
}
